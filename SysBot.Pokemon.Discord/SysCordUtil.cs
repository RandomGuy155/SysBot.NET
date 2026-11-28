using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using SysBot.Base;

namespace SysBot.Pokemon.Discord;

internal static class SysCordUtil
{
    public static Task Log(LogMessage msg, GameMode? mode)
    {
        var text = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
        Console.ForegroundColor = GetTextColor(msg.Severity);
        Console.WriteLine($"{DateTime.Now,-19} {text}");
        Console.ResetColor();
        var gameTag = mode?.ToString();
        LogUtil.LogText($"SysCord: {text}", gameTag);
        return Task.CompletedTask;
    }

    public static ConsoleColor GetTextColor(LogSeverity sv) => sv switch
    {
        LogSeverity.Critical => ConsoleColor.Red,
        LogSeverity.Error => ConsoleColor.Red,
        LogSeverity.Warning => ConsoleColor.Yellow,
        LogSeverity.Info => ConsoleColor.White,
        LogSeverity.Verbose => ConsoleColor.DarkGray,
        LogSeverity.Debug => ConsoleColor.DarkGray,
        _ => Console.ForegroundColor,
    };

    public static Task LogInteractionStart(SocketInteractionContext context, ulong channelId, GameMode? mode)
    {
        var interaction = context.Interaction;
        var (type, identity) = interaction switch
        {
            SocketSlashCommand cmd => ("slash command", $"Command: {cmd.CommandName}"),
            SocketModal modal => ("modal", $"Modal: {modal.Id}"),
            SocketMessageComponent c => ("component", $"Component: {c.Id}"),
            _ => (interaction.Type.ToString(), "Unknown"),
        };
        var channel = interaction.Channel?.Name ?? $"Unknown Channel: {channelId}";
        var location = interaction.IsDMInteraction ? "Direct Messages" : context.Guild?.Name ?? "Unknown Guild";
        var status = $"Executing {type} from {location}:{channel}:@{context.User.Username}. {identity}";

        return Log(GetLog(LogSeverity.Info, status), mode);
    }

    public static Task RespondErrorAsync(SocketInteraction interaction, string message, bool ephemeral = true) => interaction.HasResponded
        ? interaction.FollowupAsync(message, ephemeral: ephemeral)
        : interaction.RespondAsync(message, ephemeral: ephemeral);

    public static LogMessage GetLog(LogSeverity severity, string message, [CallerMemberName] string identity = "")
        => new(severity, identity, message);

    public static string ComputeSlashCommandHash(InteractionService interactions, ulong guildId)
    {
        var commands = interactions.SlashCommands;
        var json = JsonSerializer.Serialize(new
        {
            GuildId = guildId,
            Commands = commands.Select(c => new
            {
                c.Name,
                c.Description,
                Params = c.Parameters.Select(p => new { p.Name, p.Description, p.DiscordOptionType }),
            }),
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static async Task<bool> RegisterSlashCommandsAsync(InteractionService interactions, DiscordSettings cfg, Action<int, int> onRegistered)
    {
        var guildId = cfg.SlashMainGuild;
        var hash = ComputeSlashCommandHash(interactions, guildId);
        if (hash == cfg.SlashCommandHash)
        {
            LogUtil.LogInfo("Skipped registering interactions; no signature changes detected.");
            onRegistered(interactions.SlashCommands.Count, interactions.ModalCommands.Count);
            return true;
        }

        try
        {
            if (guildId != 0)
            {
                await interactions.RegisterCommandsToGuildAsync(guildId, deleteMissing: true).ConfigureAwait(false);
                LogUtil.LogInfo($"Registered guild interactions for {guildId}. Hash: {hash[..6]}");
            }
            else
            {
                await interactions.RegisterCommandsGloballyAsync(deleteMissing: true).ConfigureAwait(false);
                LogUtil.LogInfo($"Registered global interactions. Hash: {hash[..6]}");
            }

            onRegistered(interactions.SlashCommands.Count, interactions.ModalCommands.Count);
            cfg.SlashCommandHash = hash;
            return true;
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex);
            return false;
        }
    }
}

