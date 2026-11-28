using System;
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

public sealed class SysCord<T> where T : PKM, new()
{
    public static PokeBotRunner<T> Runner { get; internal set; } = null!;

    private readonly DiscordSocketClient _client;
    private readonly DiscordManager _manager;
    private readonly SysCordContext _context;
    public readonly PokeTradeHub<T> Hub;

    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;

    private bool MessageChannelsLoaded { get; set; }
    private bool CommandsRegistered { get; set; }

    public SysCord(PokeBotRunner<T> runner)
    {
        Runner = runner;
        Hub = runner.Hub;
        _manager = new DiscordManager(Hub.Config.Discord);

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            GatewayIntents = Guilds | GuildMessages | DirectMessages,
        });

        _context = new SysCordContext(_manager, Hub.Config, _client);

        _interactions = new InteractionService(_client.Rest, new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Info,
            DefaultRunMode = Hub.Config.Discord.AsyncCommands ? RunMode.Async : RunMode.Sync,
        });

        _client.Log += Log;
        _interactions.Log += Log;
        _services = ConfigureServices();
        _context.ServiceProvider = _services;
        SysCordSettings.Current = _context;
    }

    private ServiceProvider ConfigureServices()
    {
        return new ServiceCollection()
            .AddSingleton(_context)
            .AddSingleton(_manager)
            .AddSingleton(Hub.Config)
            .AddSingleton(_client)
            .AddSingleton(_interactions)
            .BuildServiceProvider();
    }

    private Task Log(LogMessage msg) => SysCordUtil.Log(msg, Runner.Mode);

    public async Task MainAsync(string apiToken, CancellationToken token)
    {
        SysCordSettings.Current = _context;
        await InitCommands().ConfigureAwait(false);
        await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);

        var app = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
        _manager.SetOwnership(app);

        await _client.StartAsync().ConfigureAwait(false);
        await MonitorStatusAsync(token).ConfigureAwait(false);
    }

    public async Task InitCommands()
    {
        // All modules are suffixed with "Module" in their class name.
        // The blacklist is a comma-separated list of module names (without the "Module" suffix) that should not be added to the bot.
        var blacklist = Hub.Config.Discord.ModuleBlacklist
            .Replace("Module", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(z => z.Trim())
            .ToList();

        var assembly = Assembly.GetExecutingAssembly();
        await LoadModulesFromAssembly(assembly, blacklist).ConfigureAwait(false);

        _client.Ready += LoadCommandsAndChannels;
        _client.InteractionCreated += HandleInteractionAsync;
    }

    private async Task LoadModulesFromAssembly(Assembly assembly, IReadOnlyList<string> blacklist)
    {
        var moduleTypes = assembly.DefinedTypes
            .Where(z => z is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(InteractionModuleBase<SocketInteractionContext>).IsAssignableFrom(z.AsType()))
            .Select(z => z.AsType());
        var genericTypes = assembly.DefinedTypes
            .Where(z => z is { IsAbstract: false, IsGenericTypeDefinition: true } && typeof(InteractionModuleBase<SocketInteractionContext>).IsAssignableFrom(z.AsType()))
            .Select(z => z.MakeGenericType(typeof(T)));

        var types = moduleTypes.Concat(genericTypes);

        foreach (var module in types)
        {
            var name = ReusableActions.GetModuleName(module.Name);
            if (IsBlacklisted(name, blacklist))
                continue;

            var registered = await _interactions.AddModuleAsync(module, _services).ConfigureAwait(false);
            LogUtil.LogInfo(
                $"Loaded module {name}: " +
                $"slash={registered?.SlashCommands.Count ?? -1}, " +
                $"modal={registered?.ModalCommands.Count ?? -1}, " +
                $"component={registered?.ComponentCommands.Count ?? -1}");
        }
    }

    private static bool IsBlacklisted(string name, IReadOnlyList<string> blacklist)
        => blacklist.Any(z => z.Equals(name, StringComparison.OrdinalIgnoreCase));

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        SysCordSettings.Current = _context;
        var context = new SocketInteractionContext(_client, interaction);

        if (!_manager.CanUseCommandUser(context.User.Id))
        {
            await SysCordUtil.RespondErrorAsync(interaction, "You are not permitted to use this command.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if ((context.Interaction.ChannelId is not { } channel) || (!_manager.CanUseCommandChannel(channel) && _manager.IsTeamOrOwner(context.User.Id)))
        {
            // Visibly reply if settings require (so that others can see).
            var ephemeral = !Hub.Config.Discord.ReplyCannotUseCommandInChannel;
            await SysCordUtil.RespondErrorAsync(interaction, "You can't use that here.", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        await SysCordUtil.LogInteractionStart(context, channel, Runner.Mode).ConfigureAwait(false);

        var result = await _interactions.ExecuteCommandAsync(context, _services).ConfigureAwait(false);
        if (!result.IsSuccess && !interaction.HasResponded)
            await SysCordUtil.RespondErrorAsync(interaction, result.ErrorReason).ConfigureAwait(false);
    }

    private async Task MonitorStatusAsync(CancellationToken token)
    {
        const int interval = 20;
        var state = UserStatus.Idle;

        while (!token.IsCancellationRequested)
        {
            var time = DateTime.Now;
            var lastLogged = LogUtil.LastLogged;
            if (Hub.Config.Discord.BotColorStatusTradeOnly)
            {
                var recent = Hub.Bots.ToArray()
                    .Where(z => z.Config.InitialRoutine.IsTradeBot())
                    .MaxBy(z => z.LastTime);
                lastLogged = recent?.LastTime ?? time;
            }
            var delta = time - lastLogged;
            var gap = TimeSpan.FromSeconds(interval) - delta;

            bool noQueue = !Hub.Queues.Info.GetCanQueue();
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

    private async Task LoadCommandsAndChannels()
    {
        SysCordSettings.Current = _context;
        var cfg = Hub.Config.Discord;
        if (!CommandsRegistered)
            CommandsRegistered = await SysCordUtil.RegisterSlashCommandsAsync(_interactions, cfg, _context.SetCommandsRegistered).ConfigureAwait(false);

        if (MessageChannelsLoaded)
            return;

        // Restore Echoes
        EchoModule.RestoreChannels(_client, cfg);

        // Restore Logging
        LogModule.RestoreLogging(_client, cfg, Hub.Config.Mode);
        TradeStartModule<T>.RestoreTradeStarting(_client, cfg);

        // Don't let it load more than once in case of Discord hiccups.
        const string status = "Logging and Echo channels loaded!";
        await Log(SysCordUtil.GetLog(LogSeverity.Info, status)).ConfigureAwait(false);
        MessageChannelsLoaded = true;

        var game = cfg.BotGameStatus;
        if (!string.IsNullOrWhiteSpace(game))
            await _client.SetGameAsync(game).ConfigureAwait(false);
    }
}
