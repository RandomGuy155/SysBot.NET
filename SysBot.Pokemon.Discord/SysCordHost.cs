using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Core;
using SysBot.Base;
using static Discord.GatewayIntents;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Hosts a single Discord bot connection that routes slash commands, modals, and components
/// to the appropriate per-game <see cref="PokeBotRunner{T}"/> based on each game's configured channel whitelist.
/// </summary>
public sealed class SysCordHost
{
    private sealed class GameRoute
    {
        public required ProgramMode Mode { get; init; }
        public required Type PkmType { get; init; }
        public required DiscordManager Manager { get; init; }
        public required SysCordContext Context { get; init; }
        public required InteractionService Interactions { get; init; }
        public required IServiceProvider Services { get; init; }
        public required Action<DiscordSocketClient> RestoreChannels { get; init; }
        public required Func<DateTime?> GetLastTradeBotTime { get; init; }
        public required Func<bool> GetCanQueue { get; init; }
        public bool ModulesLoaded { get; set; }
        public bool ChannelsRestored { get; set; }
    }

    private readonly ProgramConfig _config;
    private readonly DiscordSettings _sharedDiscord;
    private readonly DiscordSocketClient _client;
    private readonly ConcurrentDictionary<ProgramMode, GameRoute> _routes = [];

    private IApplication? _application;
    private bool _commandsRegistered;
    private bool _started;

    public SysCordHost(ProgramConfig config)
    {
        _config = config;
        _sharedDiscord = config.Hub.Discord;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            GatewayIntents = Guilds | GuildMessages | DirectMessages,
        });

        _client.Log += msg => SysCordUtil.Log(msg, null);
    }

    public void RegisterRunner<T>(PokeBotRunner<T> runner) where T : PKM, new()
    {
        var mode = runner.Mode;
        if (mode == ProgramMode.None || _routes.ContainsKey(mode))
            return;

        SysCord<T>.Runner = runner;
        var hub = runner.Hub;
        var manager = new DiscordManager(hub.Config.Discord);
        var context = new SysCordContext(manager, hub.Config, _client, _config);

        var interactions = new InteractionService(_client.Rest, new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Info,
            DefaultRunMode = _sharedDiscord.AsyncCommands ? RunMode.Async : RunMode.Sync,
        });
        interactions.Log += msg => SysCordUtil.Log(msg, mode);

        var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton(manager)
            .AddSingleton(hub.Config)
            .AddSingleton(_client)
            .AddSingleton(interactions)
            .BuildServiceProvider();

        context.ServiceProvider = services;

        if (_application is not null)
            manager.SetOwnership(_application);

        var route = new GameRoute
        {
            Mode = mode,
            PkmType = typeof(T),
            Manager = manager,
            Context = context,
            Interactions = interactions,
            Services = services,
            RestoreChannels = client =>
            {
                EchoModule.RestoreChannels(client, hub.Config.Discord);
                LogModule.RestoreLogging(client, hub.Config.Discord);
                TradeStartModule<T>.RestoreTradeStarting(client, hub.Config.Discord);
            },
            GetLastTradeBotTime = () => hub.Bots.ToArray()
                .Where(z => z.Config.InitialRoutine.IsTradeBot())
                .MaxBy(z => z.LastTime)?.LastTime,
            GetCanQueue = () => hub.Queues.Info.GetCanQueue(),
        };

        _routes[mode] = route;
        SysCordSettings.Current ??= context;

        if (_started)
            _ = Task.Run(() => InitializeLateRouteAsync(route));
    }

    private async Task InitializeLateRouteAsync(GameRoute route)
    {
        try
        {
            SysCordSettings.Current = route.Context;
            await LoadModulesForRouteAsync(route).ConfigureAwait(false);
            if (_client.ConnectionState == ConnectionState.Connected && !route.ChannelsRestored)
            {
                route.RestoreChannels(_client);
                route.ChannelsRestored = true;
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex);
        }
    }

    public async Task MainAsync(string apiToken, CancellationToken token)
    {
        _started = true;
        await InitCommandsAsync().ConfigureAwait(false);
        await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);

        _application = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
        foreach (var route in _routes.Values)
            route.Manager.SetOwnership(_application);

        await _client.StartAsync().ConfigureAwait(false);
        await MonitorStatusAsync(token).ConfigureAwait(false);
    }

    private async Task InitCommandsAsync()
    {
        foreach (var route in _routes.Values)
            await LoadModulesForRouteAsync(route).ConfigureAwait(false);

        _client.Ready += LoadCommandsAndChannels;
        _client.InteractionCreated += HandleInteractionAsync;
    }

    private async Task LoadModulesForRouteAsync(GameRoute route)
    {
        if (route.ModulesLoaded)
            return;

        var blacklist = _sharedDiscord.ModuleBlacklist
            .Replace("Module", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(z => z.Trim())
            .ToList();

        var assembly = Assembly.GetExecutingAssembly();
        var moduleTypes = assembly.DefinedTypes
            .Where(z => z is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(InteractionModuleBase<SocketInteractionContext>).IsAssignableFrom(z.AsType()))
            .Select(z => z.AsType());
        var genericTypes = assembly.DefinedTypes
            .Where(z => z is { IsAbstract: false, IsGenericTypeDefinition: true } && typeof(InteractionModuleBase<SocketInteractionContext>).IsAssignableFrom(z.AsType()))
            .Select(z => z.MakeGenericType(route.PkmType));

        foreach (var module in moduleTypes.Concat(genericTypes))
        {
            var name = ReusableActions.GetModuleName(module.Name);
            if (blacklist.Any(z => z.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            var registered = await route.Interactions.AddModuleAsync(module, route.Services).ConfigureAwait(false);
            LogUtil.LogInfo(
                $"[{route.Mode}] Loaded module {name}: " +
                $"slash={registered?.SlashCommands.Count ?? -1}, " +
                $"modal={registered?.ModalCommands.Count ?? -1}, " +
                $"component={registered?.ComponentCommands.Count ?? -1}");
        }

        route.ModulesLoaded = true;
    }

    private GameRoute? ResolveRoute(SocketInteraction interaction, out bool isOwnerFallback)
    {
        isOwnerFallback = false;
        var activeModes = _routes.Keys.ToArray();
        if (activeModes.Length == 0)
            return null;

        if (interaction.ChannelId is { } channelId)
        {
            var matched = _config.ResolveModeForChannel(channelId, activeModes);
            if (matched is { } mode && _routes.TryGetValue(mode, out var route))
                return route;
        }

        // Allow bot owners / team members to run commands in DMs or unmapped channels using the default game.
        var fallback = _routes.TryGetValue(_config.Mode, out var def) ? def : _routes.Values.First();
        if (fallback.Manager.IsTeamOrOwner(interaction.User.Id))
        {
            isOwnerFallback = true;
            return fallback;
        }

        return null;
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(_client, interaction);

        var route = ResolveRoute(interaction, out var isOwnerFallback);
        if (route is null)
        {
            var ephemeral = !_sharedDiscord.ReplyCannotUseCommandInChannel;
            await SysCordUtil.RespondErrorAsync(interaction, "You can't use that here.", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (!route.Manager.CanUseCommandUser(context.User.Id))
        {
            await SysCordUtil.RespondErrorAsync(interaction, "You are not permitted to use this command.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (isOwnerFallback && _routes.Count > 1 && interaction is SocketSlashCommand { CommandName: not "owner" })
        {
            LogUtil.LogInfo($"Owner command in unassigned channel routed to {route.Mode}. Use /owner add-channel to assign this channel.");
        }

        SysCordSettings.Current = route.Context;
        await SysCordUtil.LogInteractionStart(context, interaction.ChannelId ?? 0, route.Mode).ConfigureAwait(false);

        var result = await route.Interactions.ExecuteCommandAsync(context, route.Services).ConfigureAwait(false);
        if (!result.IsSuccess && !interaction.HasResponded)
            await SysCordUtil.RespondErrorAsync(interaction, result.ErrorReason).ConfigureAwait(false);
    }

    private void ValidateChannelRouting()
    {
        if (_routes.Count <= 1)
            return;

        var seen = new Dictionary<ulong, ProgramMode>();
        foreach (var mode in _routes.Keys.Order())
        {
            var list = _config.GetGameHubSettings(mode).ChannelWhitelist.List;
            if (list.Count == 0)
            {
                LogUtil.LogError($"[{mode}] No channels assigned in ChannelWhitelist. Commands will not route to {mode} until a channel is assigned (use /owner add-channel or Hub -> {mode}).");
                continue;
            }

            foreach (var entry in list)
            {
                if (seen.TryGetValue(entry.ID, out var existing))
                    LogUtil.LogError($"Channel {entry.ID} ({entry.Name}) is assigned to multiple active games ({existing}, {mode}); commands will route to {existing}.");
                else
                    seen[entry.ID] = mode;
            }
        }
    }

    private async Task LoadCommandsAndChannels()
    {
        ValidateChannelRouting();

        var primaryRoute = _routes.TryGetValue(_config.Mode, out var def) ? def : _routes.Values.FirstOrDefault();
        if (primaryRoute is not null && !_commandsRegistered)
            _commandsRegistered = await SysCordUtil.RegisterSlashCommandsAsync(primaryRoute.Interactions, _sharedDiscord, UpdateRegisteredCounts).ConfigureAwait(false);

        foreach (var route in _routes.Values)
        {
            if (route.ChannelsRestored)
                continue;

            SysCordSettings.Current = route.Context;
            route.RestoreChannels(_client);
            route.ChannelsRestored = true;
            await SysCordUtil.Log(SysCordUtil.GetLog(LogSeverity.Info, "Logging and Echo channels loaded!"), route.Mode).ConfigureAwait(false);
        }

        var game = _sharedDiscord.BotGameStatus;
        if (!string.IsNullOrWhiteSpace(game))
            await _client.SetGameAsync(game).ConfigureAwait(false);
    }

    private void UpdateRegisteredCounts(int commands, int modals)
    {
        foreach (var route in _routes.Values)
            route.Context.SetCommandsRegistered(commands, modals);
    }

    private async Task MonitorStatusAsync(CancellationToken token)
    {
        const int interval = 20;
        var state = UserStatus.Idle;

        while (!token.IsCancellationRequested)
        {
            var time = DateTime.Now;
            var lastLogged = LogUtil.LastLogged;
            if (_sharedDiscord.BotColorStatusTradeOnly)
            {
                DateTime? mostRecent = null;
                foreach (var route in _routes.Values)
                {
                    var t = route.GetLastTradeBotTime();
                    if (t is not null && (mostRecent is null || t > mostRecent))
                        mostRecent = t;
                }
                lastLogged = mostRecent ?? time;
            }
            var delta = time - lastLogged;
            var gap = TimeSpan.FromSeconds(interval) - delta;

            bool noQueue = _routes.Values.All(r => !r.GetCanQueue());
            if (gap <= TimeSpan.Zero)
            {
                var idle = noQueue ? UserStatus.DoNotDisturb : UserStatus.Idle;
                if (idle != state)
                {
                    state = idle;
                    await _client.SetStatusAsync(state).ConfigureAwait(false);
                }
                await Task.Delay(2_000, token).ConfigureAwait(false);
                continue;
            }

            var active = noQueue ? UserStatus.DoNotDisturb : UserStatus.Online;
            if (active != state)
            {
                state = active;
                await _client.SetStatusAsync(state).ConfigureAwait(false);
            }
            await Task.Delay(gap, token).ConfigureAwait(false);
        }
    }
}

