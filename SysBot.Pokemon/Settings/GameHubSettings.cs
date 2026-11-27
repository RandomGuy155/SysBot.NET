using System.ComponentModel;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SysBot.Pokemon;

/// <summary>
/// Per-game settings that vary between game hubs when running multiple games in a single process.
/// </summary>
public sealed class GameHubSettings
{
    private const string Discord = nameof(Discord);
    private const string Operation = nameof(Operation);
    private const string BotTrade = nameof(BotTrade);

    // Discord (per-game channel routing & greeting)

    [Category(Discord), Description("Channels with these IDs route Discord commands to this game's bots.")]
    public RemoteControlAccessList ChannelWhitelist { get; set; } = new();

    [Category(Discord), Description("Custom message the bot will reply with when a user says hello in this game's channels. Use string formatting to mention the user in the reply.")]
    public string HelloResponse { get; set; } = "Hi {0}!";

    [Category(Discord), Description("Channel IDs that will echo the log bot data.")]
    public RemoteControlAccessList LoggingChannels { get; set; } = new();

    [Category(Discord), Description("Logger channels that will log trade start messages for this game.")]
    public RemoteControlAccessList TradeStartingChannels { get; set; } = new();

    [Category(Discord), Description("Echo channels that will log special messages.")]
    public RemoteControlAccessList EchoChannels { get; set; } = new();

    // Operation

    [Category(Operation)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public QueueSettings Queues { get; set; } = new();

    [Category(Operation), Description("Add extra time for slower Switches.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public TimingSettings Timings { get; set; } = new();

    // Trade

    [Category(BotTrade), Description("Settings for idle distribution trades.")]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public DistributionSettings Distribution { get; set; } = new();

    public static GameHubSettings FromHub(PokeTradeHubConfig hub) => new()
    {
        ChannelWhitelist = hub.Discord.ChannelWhitelist,
        HelloResponse = hub.Discord.HelloResponse,
        LoggingChannels = hub.Discord.LoggingChannels,
        TradeStartingChannels = hub.Discord.TradeStartingChannels,
        EchoChannels = hub.Discord.EchoChannels,
        Queues = hub.Queues,
        Timings = hub.Timings,
        Distribution = hub.Distribution,
    };

    public void CopyToHub(PokeTradeHubConfig hub)
    {
        hub.Discord.ChannelWhitelist = ChannelWhitelist;
        hub.Discord.HelloResponse = HelloResponse;
        hub.Discord.LoggingChannels = LoggingChannels;
        hub.Discord.TradeStartingChannels = TradeStartingChannels;
        hub.Discord.EchoChannels = EchoChannels;
        hub.Queues = Queues;
        hub.Timings = Timings;
        hub.Distribution = Distribution;
    }
}
