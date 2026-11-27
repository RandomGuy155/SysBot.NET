using System.Collections.Generic;
using System.Text.Json.Serialization;
using SysBot.Base;

namespace SysBot.Pokemon;

public class ProgramConfig : BotList<PokeBotState>
{
    public ProgramMode Mode { get; set; } = ProgramMode.LZA;
    public PokeTradeHubConfig Hub { get; set; } = new();
    public Dictionary<ProgramMode, GameHubSettings> GameHubs { get; set; } = [];
    public bool? DarkMode { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public ProgramMode GetEffectiveMode(PokeBotState bot) => bot.Mode is { } m && m != ProgramMode.None ? m : Mode;

    public GameHubSettings GetGameHubSettings(ProgramMode mode)
    {
        if (GameHubs.TryGetValue(mode, out var existing))
            return existing;

        var created = mode == Mode ? GameHubSettings.FromHub(Hub) : new GameHubSettings();
        GameHubs[mode] = created;
        return created;
    }

    /// <summary>
    /// Ensures legacy <see cref="Hub"/> settings are migrated into <see cref="GameHubs"/> for the primary <see cref="Mode"/>,
    /// and mirrors the primary game's settings back onto <see cref="Hub"/> for backwards compatibility.
    /// </summary>
    public void EnsurePrimaryHubSynced()
    {
        if (Mode == ProgramMode.None)
            return;

        if (!GameHubs.TryGetValue(Mode, out var primary))
        {
            primary = GameHubSettings.FromHub(Hub);
            GameHubs[Mode] = primary;
        }
        else
        {
            primary.CopyToHub(Hub);
        }
    }

    public PokeTradeHubConfig BuildHubConfig(ProgramMode mode)
    {
        var config = new PokeTradeHubConfig { Mode = mode };
        SyncHubConfig(config);
        return config;
    }

    public void SyncHubConfig(PokeTradeHubConfig target)
    {
        var mode = target.Mode != ProgramMode.None ? target.Mode : Mode;
        target.Mode = mode;
        var game = GetGameHubSettings(mode);

        // Shared scalar BaseConfig settings
        target.AntiIdle = Hub.AntiIdle;
        target.LoggingEnabled = Hub.LoggingEnabled;
        target.MaxArchiveFiles = Hub.MaxArchiveFiles;
        target.SkipConsoleBotCreation = Hub.SkipConsoleBotCreation;

        // Shared reference settings so runtime mutations apply to all games immediately
        target.Legality = Hub.Legality;
        target.Folder = Hub.Folder;
        target.Trade = Hub.Trade;
        target.SeedCheckSWSH = Hub.SeedCheckSWSH;
        target.TradeAbuse = Hub.TradeAbuse;
        target.EncounterSWSH = Hub.EncounterSWSH;
        target.RaidSWSH = Hub.RaidSWSH;
        target.StopConditions = Hub.StopConditions;
        target.Twitch = Hub.Twitch;
        target.YouTube = Hub.YouTube;
        target.Stream = Hub.Stream;
        target.Favoritism = Hub.Favoritism;

        // Per-game settings
        target.Queues = game.Queues;
        target.Timings = game.Timings;
        target.Distribution = game.Distribution;

        // Discord: per-game channel routing & greeting + shared bot credentials/roles/lists by reference
        var d = target.Discord;
        d.HelloResponse = game.HelloResponse;
        d.ChannelWhitelist = game.ChannelWhitelist;
        d.LoggingChannels = game.LoggingChannels;
        d.TradeStartingChannels = game.TradeStartingChannels;
        d.EchoChannels = game.EchoChannels;

        var shared = Hub.Discord;
        d.Token = shared.Token;
        d.BotGameStatus = shared.BotGameStatus;
        d.ModuleBlacklist = shared.ModuleBlacklist;
        d.SlashCommandHash = shared.SlashCommandHash;
        d.SlashMainGuild = shared.SlashMainGuild;
        d.AsyncCommands = shared.AsyncCommands;
        d.BotColorStatusTradeOnly = shared.BotColorStatusTradeOnly;
        d.AllowGlobalSudo = shared.AllowGlobalSudo;
        d.ReturnPKMs = shared.ReturnPKMs;
        d.ReplyCannotUseCommandInChannel = shared.ReplyCannotUseCommandInChannel;
        d.RoleCanTrade = shared.RoleCanTrade;
        d.RoleCanSeedCheck = shared.RoleCanSeedCheck;
        d.RoleCanClone = shared.RoleCanClone;
        d.RoleCanDump = shared.RoleCanDump;
        d.RoleRemoteControl = shared.RoleRemoteControl;
        d.RoleSudo = shared.RoleSudo;
        d.RoleFavored = shared.RoleFavored;
        d.UserBlacklist = shared.UserBlacklist;
        d.GlobalSudoList = shared.GlobalSudoList;
    }

    /// <summary>
    /// Resolves which active <see cref="ProgramMode"/> owns the given Discord <paramref name="channelId"/>
    /// based on each game's <see cref="GameHubSettings.ChannelWhitelist"/>.
    /// </summary>
    public ProgramMode? ResolveModeForChannel(ulong channelId, IReadOnlyCollection<ProgramMode> activeModes)
    {
        foreach (var mode in activeModes)
        {
            if (GameHubs.TryGetValue(mode, out var settings) && settings.ChannelWhitelist.Contains(channelId))
                return mode;
        }

        if (activeModes.Count == 1)
        {
            foreach (var only in activeModes)
            {
                if (!GameHubs.TryGetValue(only, out var settings) || settings.ChannelWhitelist.List.Count == 0)
                    return only;
            }
        }

        return null;
    }
}

public enum ProgramMode
{
    None = 0, // invalid
    SWSH = 1,
    BDSP = 2,
    LA = 3,
    SV = 4,
    LZA = 5,
}

[JsonSerializable(typeof(ProgramConfig))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class ProgramConfigContext : JsonSerializerContext;
