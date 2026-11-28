using System;
using System.Runtime.CompilerServices;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.Pokemon.Discord;

public sealed record ChannelLogger(ISocketMessageChannel Channel, GameMode Mode) : ILogForwarder
{
    public void Forward(string message, [CallerMemberName] string identity = "")
    {
        if (LogUtil.GameTagResolver is { } resolver)
        {
            var tag = resolver(identity);
            if (!string.Equals(tag, Mode.ToString(), StringComparison.OrdinalIgnoreCase))
                return;
        }

        try
        {
            var text = GetMessage(message, identity);
            Channel.SendMessageAsync(text);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, identity);
        }
    }

    private static string GetMessage(ReadOnlySpan<char> msg, string identity)
        => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";
}
