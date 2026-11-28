using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Discord.Interactions;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.Pokemon.Discord;

[Group("echo", "Control over bot echoes.")]
public class EchoModule(SysCordContext cord) : SudoModuleBase
{
    // ReSharper disable NotAccessedPositionalProperty.Local
    private record EchoChannel(ulong ChannelId, string ChannelName, GameMode Mode, Action<string, string?> Action);

    private static readonly ConcurrentDictionary<(ulong ChannelId, GameMode Mode), EchoChannel> Channels = [];

    public static void RestoreChannels(DiscordSocketClient discord, DiscordSettings cfg, GameMode mode)
    {
        int count = 0;
        foreach (var channelAccess in cfg.EchoChannels)
        {
            if (Channels.ContainsKey((channelAccess.ID, mode)))
                continue;
            if (discord.GetChannel(channelAccess.ID) is not ISocketMessageChannel channel)
            {
                LogUtil.LogInfo($"Failed to add echoes to {channelAccess.Name}.");
                continue;
            }

            AddEchoChannel(channel, channelAccess.ID, mode);
            count++;
        }

        EchoUtil.Echo($"Added echo notification to {count} Discord channel(s) on Bot startup.", mode.ToString());
    }

    [SlashCommand("here", "Makes the bot echo special messages to this channel.")]
    public async Task AddEchoAsync()
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
            await RespondAsync("Already notifying here.").ConfigureAwait(false);
            return;
        }

        AddEchoChannel(channel, channelId, mode);
        cord.Settings.EchoChannels.AddIfNew(GetReference(channel));
        await RespondAsync("Added Echo output to this channel!").ConfigureAwait(false);
    }

    private static void AddEchoChannel(ISocketMessageChannel channel, ulong channelId, GameMode mode)
    {
        Action<string, string?> l = Echo;
        if (Channels.TryAdd((channelId, mode), new EchoChannel(channelId, channel.Name, mode, l)))
            EchoUtil.TaggedForwarders.Add(l);
        return;

        void Echo(string message, string? gameTag)
        {
            if (gameTag is not null && !string.Equals(gameTag, mode.ToString(), StringComparison.OrdinalIgnoreCase))
                return;
            channel.SendMessageAsync(message);
        }
    }

    public static bool IsEchoChannel(ISocketMessageChannel channel) => Channels.Keys.Any(k => k.ChannelId == channel.Id);

    [SlashCommand("info", "Dumps the Echo settings.")]
    public async Task DumpEchoInfoAsync()
    {
        var mode = cord.HubConfig.Mode;
        var ids = cord.Settings.EchoChannels.List.Select(z => z.ID).ToHashSet();
        var entries = Channels.Where(c => c.Key.Mode == mode && ids.Contains(c.Key.ChannelId)).Select(c => $"{c.Key.ChannelId} - {c.Value}");
        var text = string.Join('\n', entries);
        await RespondAsync(string.IsNullOrEmpty(text) ? "No echo channels configured." : text).ConfigureAwait(false);
    }

    [SlashCommand("clear", "Clears Echo settings from this channel.")]
    public async Task ClearEchosAsync()
    {
        var channelId = Context.Interaction.Channel.Id;
        var key = (channelId, cord.HubConfig.Mode);
        if (!Channels.TryRemove(key, out var echo))
        {
            await RespondAsync("Not echoing in this channel.").ConfigureAwait(false);
            return;
        }

        EchoUtil.TaggedForwarders.Remove(echo.Action);
        cord.Settings.EchoChannels.RemoveAll(z => z.ID == channelId);
        await RespondAsync($"Echoes cleared from channel: {Context.Interaction.Channel.Name}").ConfigureAwait(false);
    }

    [SlashCommand("clear-all", "Clears all Echo channel settings.")]
    public async Task ClearEchosAllAsync()
    {
        var mode = cord.HubConfig.Mode;
        var ids = cord.Settings.EchoChannels.List.Select(z => z.ID).ToHashSet();
        foreach (var id in ids)
        {
            if (Channels.TryRemove((id, mode), out var l))
                EchoUtil.TaggedForwarders.Remove(l.Action);
        }

        cord.Settings.EchoChannels.Clear();
        await RespondAsync("Echoes cleared from all channels!").ConfigureAwait(false);
    }
}
