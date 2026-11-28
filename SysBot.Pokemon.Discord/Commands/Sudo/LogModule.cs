using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Interactions;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.Pokemon.Discord;

[Group("log", "Logging commands.")]
public class LogModule(SysCordContext cord) : SudoModuleBase
{
    private static readonly Dictionary<(ulong ChannelId, ProgramMode Mode), ChannelLogger> Channels = [];
    public static void RestoreLogging(DiscordSocketClient discord, DiscordSettings settings, ProgramMode mode = ProgramMode.None)
    {
        int count = 0;
        foreach (var channelAccess in settings.LoggingChannels)
        {
            if (Channels.ContainsKey((channelAccess.ID, mode)))
                continue;
            if (discord.GetChannel(channelAccess.ID) is not ISocketMessageChannel channel)
            {
                LogUtil.LogInfo($"Failed to add logging to {channelAccess.Name}.");
                continue;
            }

            AddLogChannel(channel, channelAccess.ID, mode);
            count++;
        }

        LogUtil.LogInfo($"Added logging to {count} Discord channel(s) on Bot startup.");
    }

    [SlashCommand("here", "Makes the bot log to this channel.")]
    public async Task AddLogAsync()
    {
        if (Context.Interaction.Channel is not { } channel)
        {
            await RespondAsync("This command must be used in a message channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var channelId = channel.Id;
        var mode = cord.HubConfig.Mode;
        if (Channels.ContainsKey((channelId, mode)))
        {
            await RespondAsync("Already logging here.").ConfigureAwait(false);
            return;
        }

        AddLogChannel(channel, channelId, mode);
        cord.Settings.LoggingChannels.AddIfNew(GetReference(channel));
        await RespondAsync("Added logging output to this channel!").ConfigureAwait(false);
    }

    private static void AddLogChannel(ISocketMessageChannel channel, ulong channelId, ProgramMode mode)
    {
        var logger = new ChannelLogger(channel, mode);
        LogUtil.Forwarders.Add(logger);
        Channels.Add((channelId, mode), logger);
    }

    [SlashCommand("info", "Dumps the logging settings.")]
    public async Task DumpLogInfoAsync()
    {
        var mode = cord.HubConfig.Mode;
        var ids = cord.Settings.LoggingChannels.List.Select(z => z.ID).ToHashSet();
        var entries = Channels.Where(c => c.Key.Mode == mode && ids.Contains(c.Key.ChannelId)).Select(c => $"{c.Key.ChannelId} - {c.Value}");
        var text = string.Join('\n', entries);
        await RespondAsync(string.IsNullOrEmpty(text) ? "No logging channels configured." : text).ConfigureAwait(false);
    }

    [SlashCommand("clear", "Clears logging from this channel.")]
    public async Task ClearLogsAsync()
    {
        var channelId = Context.Interaction.Channel.Id;
        var key = (channelId, cord.HubConfig.Mode);
        if (!Channels.TryGetValue(key, out var log))
        {
            await RespondAsync("Not logging in this channel.").ConfigureAwait(false);
            return;
        }
        LogUtil.Forwarders.Remove(log);
        Channels.Remove(key);
        cord.Settings.LoggingChannels.RemoveAll(z => z.ID == channelId);
        await RespondAsync($"Logging cleared from channel: {Context.Interaction.Channel.Name}").ConfigureAwait(false);
    }

    [SlashCommand("clear-all", "Clears all logging settings.")]
    public async Task ClearLogsAllAsync()
    {
        var mode = cord.HubConfig.Mode;
        var ids = cord.Settings.LoggingChannels.List.Select(z => z.ID).ToHashSet();
        foreach (var id in ids)
        {
            if (Channels.Remove((id, mode), out var l))
                LogUtil.Forwarders.Remove(l);
        }

        cord.Settings.LoggingChannels.Clear();
        await RespondAsync("Logging cleared from all channels!").ConfigureAwait(false);
    }
}
