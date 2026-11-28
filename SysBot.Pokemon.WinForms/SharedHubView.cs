using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SysBot.Pokemon.WinForms;

/// <summary>
/// Wraps a settings object for <see cref="System.Windows.Forms.PropertyGrid"/> while exposing only
/// properties that match <paramref name="isVisible"/>, preserving all original attributes and backing storage.
/// </summary>
public sealed class FilteredSettingsView<T>(T target, Func<string, bool> isVisible, Func<T, string>? toString = null) : CustomTypeDescriptor where T : class
{
    public override string ToString() => toString?.Invoke(target) ?? target.ToString() ?? string.Empty;

    public override PropertyDescriptorCollection GetProperties()
        => Filter(TypeDescriptor.GetProperties(target));

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        => Filter(TypeDescriptor.GetProperties(target, attributes));

    public override object GetPropertyOwner(PropertyDescriptor? pd) => target;

    private PropertyDescriptorCollection Filter(PropertyDescriptorCollection source)
    {
        var list = new List<PropertyDescriptor>(source.Count);
        foreach (PropertyDescriptor prop in source)
        {
            if (isVisible(prop.Name))
                list.Add(prop);
        }
        return new PropertyDescriptorCollection([.. list], readOnly: true);
    }
}

/// <summary>
/// Helpers defining which settings properties belong only to specific games.
/// </summary>
internal static class GameSettingsFilter
{
    private static readonly HashSet<string> SwshOnlyTimings =
    [
        nameof(TimingSettings.ExtraTimeLoadRaid),
        nameof(TimingSettings.ExtraTimeOpenRaid),
        nameof(TimingSettings.ExtraTimeEndRaid),
        nameof(TimingSettings.ExtraTimeAddFriend),
        nameof(TimingSettings.ExtraTimeDeleteFriend),
    ];

    private static readonly HashSet<string> BdspOnlyTimings =
    [
        nameof(TimingSettings.ExtraTimeLeaveUnionRoom),
        nameof(TimingSettings.ExtraTimeOpenYMenu),
        nameof(TimingSettings.ExtraTimeJoinUnionRoom),
    ];

    private static readonly HashSet<string> SwshOnlyQueues =
    [
        nameof(QueueSettings.YieldMultCountSeedCheck),
        nameof(QueueSettings.YieldMultWaitSeedCheck),
    ];

    private static readonly HashSet<string> SwshOnlyTradeCounts =
    [
        nameof(TradeSettings.CompletedSurprise),
        nameof(TradeSettings.CompletedSeedChecks),
    ];

    public static bool IsTimingVisible(GameMode mode, string propName)
    {
        if (SwshOnlyTimings.Contains(propName))
            return mode == GameMode.SWSH;
        if (BdspOnlyTimings.Contains(propName))
            return mode == GameMode.BDSP;
        if (propName == nameof(TimingSettings.ExtraTimeLoadPortal))
            return mode == GameMode.SV;
        if (propName == nameof(TimingSettings.ExtraTimeConnectOnline))
            return mode is GameMode.SWSH or GameMode.SV;
        return true;
    }

    public static bool IsDistributionVisible(GameMode mode, string propName)
    {
        if (propName == nameof(DistributionSettings.RemainInUnionRoomBDSP))
            return mode == GameMode.BDSP;
        return true;
    }

    public static bool IsQueueVisible(GameMode mode, string propName)
    {
        if (SwshOnlyQueues.Contains(propName))
            return mode == GameMode.SWSH;
        return true;
    }

    public static bool IsSharedLegalityVisible(string propName)
        => propName != nameof(LegalitySettings.SetBattleVersion);

    public static bool IsSharedTradeVisible(string propName)
        => !SwshOnlyTradeCounts.Contains(propName);

    public static bool IsChannelListVisible(string propName)
        => propName != nameof(RemoteControlAccessList.AllowIfEmpty);

    public static string FormatChannelList(RemoteControlAccessList list)
        => list.List.Count == 0 ? "No channels assigned." : $"{list.List.Count} channel(s) assigned.";
}

/// <summary>
/// PropertyGrid projection of <see cref="ProgramConfig"/> showing only settings shared across all game hubs.
/// </summary>
public sealed class SharedHubView(ProgramConfig config)
{
    private const string FeatureToggle = nameof(FeatureToggle);
    private const string Operation = nameof(Operation);
    private const string Debug = nameof(Debug);
    private const string BotTrade = nameof(BotTrade);
    private const string Integration = nameof(Integration);

    private PokeTradeHubConfig Hub => config.Hub;

    [Category(Operation), Description("Default game mode used for legacy bots without an explicit game mode.")]
    public GameMode DefaultMode
    {
        get => config.Mode;
        set
        {
            if (value != GameMode.None)
                config.Mode = value;
        }
    }

    [Category(FeatureToggle), Description("When enabled, the bot will press the B button occasionally when it is not processing anything (to avoid sleep).")]
    public bool AntiIdle
    {
        get => Hub.AntiIdle;
        set => Hub.AntiIdle = value;
    }

    [Category(FeatureToggle), Description("Enables text logs. Restart to apply changes.")]
    public bool LoggingEnabled
    {
        get => Hub.LoggingEnabled;
        set => Hub.LoggingEnabled = value;
    }

    [Category(FeatureToggle), Description("Maximum number of old text log files to retain. Set this to <= 0 to disable log cleanup. Restart to apply changes.")]
    public int MaxArchiveFiles
    {
        get => Hub.MaxArchiveFiles;
        set => Hub.MaxArchiveFiles = value;
    }

    [Category(Debug), Description("Skips creating bots when the program is started; helpful for testing integrations.")]
    public bool SkipConsoleBotCreation
    {
        get => Hub.SkipConsoleBotCreation;
        set => Hub.SkipConsoleBotCreation = value;
    }

    [Category(Operation)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<LegalitySettings> Legality { get; } = new(config.Hub.Legality, GameSettingsFilter.IsSharedLegalityVisible);

    [Category(Operation)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FolderSettings Folder => Hub.Folder;

    [Category(BotTrade)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<TradeSettings> Trade { get; } = new(config.Hub.Trade, GameSettingsFilter.IsSharedTradeVisible);

    [Category(BotTrade)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public TradeAbuseSettings TradeAbuse => Hub.TradeAbuse;

    [Category(Integration), Description("Shared Discord bot settings (token, status, roles, sudo lists, blacklist). Assign per-game channels in each game's Hub view.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public SharedDiscordView Discord { get; } = new(config.Hub.Discord);

    [Category(Integration)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public TwitchSettings Twitch => Hub.Twitch;

    [Category(Integration)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public YouTubeSettings YouTube => Hub.YouTube;

    [Category(Integration), Description("Configure generation of assets for streaming.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public StreamSettings Stream => Hub.Stream;

    [Category(Integration), Description("Allows favored users to join the queue with a more favorable position than unfavored users.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FavoredPrioritySettings Favoritism => Hub.Favoritism;
}

/// <summary>
/// PropertyGrid projection of <see cref="DiscordSettings"/> exposing only settings shared across all game hubs.
/// </summary>
public sealed class SharedDiscordView(DiscordSettings discord)
{
    private const string Startup = nameof(Startup);
    private const string Operation = nameof(Operation);
    private const string Roles = nameof(Roles);
    private const string Users = nameof(Users);

    public override string ToString() => "Shared Discord Settings";

    [Category(Startup), Description("Bot login token.")]
    public string Token
    {
        get => discord.Token;
        set => discord.Token = value;
    }

    [Category(Startup), Description("List of modules that will not be loaded when the bot is started (comma separated).")]
    public string ModuleBlacklist
    {
        get => discord.ModuleBlacklist;
        set => discord.ModuleBlacklist = value;
    }

    [Category(Startup), Description("Toggle to handle commands asynchronously or synchronously.")]
    public bool AsyncCommands
    {
        get => discord.AsyncCommands;
        set => discord.AsyncCommands = value;
    }

    [Category(Startup), Description("Custom Status for playing a game.")]
    public string BotGameStatus
    {
        get => discord.BotGameStatus;
        set => discord.BotGameStatus = value;
    }

    [Category(Startup), Description("Indicates the Discord presence status color only considering bots that are Trade-type.")]
    public bool BotColorStatusTradeOnly
    {
        get => discord.BotColorStatusTradeOnly;
        set => discord.BotColorStatusTradeOnly = value;
    }

    [Category(Startup), Description("Unique hash identity of the slash command set most recently registered to Discord. Clear this string to force a refresh on next startup.")]
    public string SlashCommandHash
    {
        get => discord.SlashCommandHash;
        set => discord.SlashCommandHash = value;
    }

    [Category(Startup), Description("Main Discord server the bot lives in, where commands will be reloaded more quickly to if you need to update them.")]
    public ulong SlashMainGuild
    {
        get => discord.SlashMainGuild;
        set => discord.SlashMainGuild = value;
    }

    [Category(Roles), Description("Users with this role are allowed to enter the Trade queue.")]
    public RemoteControlAccessList RoleCanTrade
    {
        get => discord.RoleCanTrade;
        set => discord.RoleCanTrade = value;
    }

    [Category(Roles), Description("Users with this role are allowed to enter the Clone queue.")]
    public RemoteControlAccessList RoleCanClone
    {
        get => discord.RoleCanClone;
        set => discord.RoleCanClone = value;
    }

    [Category(Roles), Description("Users with this role are allowed to enter the Dump queue.")]
    public RemoteControlAccessList RoleCanDump
    {
        get => discord.RoleCanDump;
        set => discord.RoleCanDump = value;
    }

    [Category(Roles), Description("Users with this role are allowed to remotely control the console (if running as Remote Control Bot.")]
    public RemoteControlAccessList RoleRemoteControl
    {
        get => discord.RoleRemoteControl;
        set => discord.RoleRemoteControl = value;
    }

    [Category(Roles), Description("Users with this role are allowed to bypass command restrictions.")]
    public RemoteControlAccessList RoleSudo
    {
        get => discord.RoleSudo;
        set => discord.RoleSudo = value;
    }

    [Category(Roles), Description("Users with this role are allowed to join the queue with a better position.")]
    public RemoteControlAccessList RoleFavored
    {
        get => discord.RoleFavored;
        set => discord.RoleFavored = value;
    }

    [Category(Users), Description("Users with these user IDs cannot use the bot.")]
    public RemoteControlAccessList UserBlacklist
    {
        get => discord.UserBlacklist;
        set => discord.UserBlacklist = value;
    }

    [Category(Users), Description("Comma separated Discord user IDs that will have sudo access to the Bot Hub.")]
    public RemoteControlAccessList GlobalSudoList
    {
        get => discord.GlobalSudoList;
        set => discord.GlobalSudoList = value;
    }

    [Category(Users), Description("Disabling this will remove global sudo support.")]
    public bool AllowGlobalSudo
    {
        get => discord.AllowGlobalSudo;
        set => discord.AllowGlobalSudo = value;
    }

    [Category(Operation), Description("Returns PKMs of Pokémon shown in the trade to the user.")]
    public bool ReturnPKMs
    {
        get => discord.ReturnPKMs;
        set => discord.ReturnPKMs = value;
    }

    [Category(Operation), Description("Replies to users if they are not allowed to use a given command in the channel. When false, the bot will silently ignore them instead.")]
    public bool ReplyCannotUseCommandInChannel
    {
        get => discord.ReplyCannotUseCommandInChannel;
        set => discord.ReplyCannotUseCommandInChannel = value;
    }
}

/// <summary>
/// PropertyGrid projection for a per-game tab filtering <see cref="TimingSettings"/>,
/// <see cref="DistributionSettings"/>, and <see cref="QueueSettings"/> to only properties used by that game.
/// </summary>
public class GameHubView(GameMode mode, GameHubSettings game)
{
    protected const string Discord = nameof(Discord);
    protected const string Operation = nameof(Operation);
    protected const string BotTrade = nameof(BotTrade);

    public static object Create(GameMode mode, ProgramConfig config)
    {
        var game = config.GetGameHubSettings(mode);
        return mode == GameMode.SWSH
            ? new SwshHubView(game, config.Hub)
            : new GameHubView(mode, game);
    }

    // Discord

    [Category(Discord), Description("Channels with these IDs route Discord commands to this game's bots.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<RemoteControlAccessList> ChannelWhitelist { get; } = new(game.ChannelWhitelist, GameSettingsFilter.IsChannelListVisible, GameSettingsFilter.FormatChannelList);

    [Category(Discord), Description("Custom message the bot will reply with when a user says hello in this game's channels. Use string formatting to mention the user in the reply.")]
    public string HelloResponse
    {
        get => game.HelloResponse;
        set => game.HelloResponse = value;
    }

    [Category(Discord), Description("Channel IDs that will echo the log bot data.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<RemoteControlAccessList> LoggingChannels { get; } = new(game.LoggingChannels, GameSettingsFilter.IsChannelListVisible, GameSettingsFilter.FormatChannelList);

    [Category(Discord), Description("Logger channels that will log trade start messages for this game.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<RemoteControlAccessList> TradeStartingChannels { get; } = new(game.TradeStartingChannels, GameSettingsFilter.IsChannelListVisible, GameSettingsFilter.FormatChannelList);

    [Category(Discord), Description("Echo channels that will log special messages.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<RemoteControlAccessList> EchoChannels { get; } = new(game.EchoChannels, GameSettingsFilter.IsChannelListVisible, GameSettingsFilter.FormatChannelList);

    // Operation

    [Category(Operation)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<QueueSettings> Queues { get; } = new(game.Queues, p => GameSettingsFilter.IsQueueVisible(mode, p));

    [Category(Operation), Description("Add extra time for slower Switches.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<TimingSettings> Timings { get; } = new(game.Timings, p => GameSettingsFilter.IsTimingVisible(mode, p));

    // Trade

    [Category(BotTrade), Description("Settings for idle distribution trades.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FilteredSettingsView<DistributionSettings> Distribution { get; } = new(game.Distribution, p => GameSettingsFilter.IsDistributionVisible(mode, p));
}

/// <summary>
/// PropertyGrid projection for the SWSH tab adding SWSH-only Encounter, Raid, StopCondition,
/// SeedCheck, BattleVersion, RoleCanSeedCheck, and Surprise/SeedCheck count settings.
/// </summary>
public sealed class SwshHubView(GameHubSettings game, PokeTradeHubConfig hub) : GameHubView(GameMode.SWSH, game)
{
    private const string BotEncounter = nameof(BotEncounter);
    private const string SwshSpecific = "SWSH";

    [Category(Discord), Description("Users with this role are allowed to enter the Seed Check queue.")]
    public RemoteControlAccessList RoleCanSeedCheck
    {
        get => hub.Discord.RoleCanSeedCheck;
        set => hub.Discord.RoleCanSeedCheck = value;
    }

    [Category(BotTrade)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public SeedCheckSettings SeedCheckSWSH => hub.SeedCheckSWSH;

    [Category(BotEncounter)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public EncounterSettings EncounterSWSH => hub.EncounterSWSH;

    [Category(BotEncounter)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public RaidSettings RaidSWSH => hub.RaidSWSH;

    [Category(BotEncounter), Description("Stop conditions for EncounterBot.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public StopConditionSettings StopConditions => hub.StopConditions;

    [Category(SwshSpecific), Description("Adds Battle Version for SWSH for using past-gen Pokémon in online competitive play.")]
    public bool SetBattleVersion
    {
        get => hub.Legality.SetBattleVersion;
        set => hub.Legality.SetBattleVersion = value;
    }

    [Category(SwshSpecific), Description("Completed Surprise Trades")]
    public int CompletedSurprise
    {
        get => hub.Trade.CompletedSurprise;
        set => hub.Trade.CompletedSurprise = value;
    }

    [Category(SwshSpecific), Description("Completed Seed Check Trades")]
    public int CompletedSeedChecks
    {
        get => hub.Trade.CompletedSeedChecks;
        set => hub.Trade.CompletedSeedChecks = value;
    }
}
