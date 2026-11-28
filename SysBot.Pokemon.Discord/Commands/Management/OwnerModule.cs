using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace SysBot.Pokemon.Discord;

[Group("owner", "Commands usable by the bot owner.")]
[DefaultMemberPermissions(GuildPermission.Administrator)] // hide these commands from the majority of users; bot Owners must have admin on server to manage.
[RequireTeamOrOwner]
public class OwnerModule(SysCordContext cord) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("add-sudo", "Adds a user to global sudo.")]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.PrivateChannel)]
    public async Task AddSudo(IUser user)
    {
        var added = cord.Settings.GlobalSudoList.AddIfNew(GetReference(user.Id, user.GlobalName));
        await RespondAsync(added ? $"Added {user.Mention} to global sudo." : $"{user.Mention} is already in global sudo.", allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [SlashCommand("remove-sudo", "Removes a user from global sudo.")]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.PrivateChannel)]
    public async Task RemoveSudo(IUser user)
    {
        var removed = cord.Settings.GlobalSudoList.RemoveAll(z => z.ID == user.Id) > 0;
        await RespondAsync(removed ? $"Removed {user.Mention} from global sudo." : $"{user.Mention} was not in global sudo.", allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [SlashCommand("add-channel", "Assigns this channel to a game's command whitelist.")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task AddChannel(
        [Summary(nameof(game), "Game to route commands from this channel to. Leave blank for the current game.")]
        [Choice("SWSH", (int)GameMode.SWSH)]
        [Choice("BDSP", (int)GameMode.BDSP)]
        [Choice("LA", (int)GameMode.LA)]
        [Choice("SV", (int)GameMode.SV)]
        [Choice("LZA", (int)GameMode.LZA)]
        int? game = null)
    {
        var c = Context.Interaction.Channel;
        var reference = GetReference(c.Id, c.Name);

        if (cord.ProgramConfig is { } prog)
        {
            var targetMode = game is { } g ? (GameMode)g : cord.HubConfig.Mode;

            // A channel can only belong to one game at a time; ask before taking it from another game.
            var assignedElsewhere = prog.GameHubs
                .Where(kv => kv.Key != targetMode && kv.Value.ChannelWhitelist.Contains(c.Id))
                .Select(kv => kv.Key)
                .ToList();

            if (assignedElsewhere.Count > 0)
            {
                var prompt = $"#{c.Name} is already assigned to **{string.Join(", ", assignedElsewhere)}**. " +
                             $"Move it to **{targetMode}**? It will be removed from the other game.";
                var buttons = new ComponentBuilder()
                    .WithButton("Move", $"{MoveChannelId}:{c.Id}:{(int)targetMode}", ButtonStyle.Danger)
                    .WithButton("Cancel", CancelId, ButtonStyle.Secondary)
                    .Build();
                await RespondAsync(prompt, components: buttons, ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (prog.GetGameHubSettings(targetMode).ChannelWhitelist.Contains(c.Id))
            {
                await RespondAsync($"#{c.Name} is already assigned to **{targetMode}**.").ConfigureAwait(false);
                return;
            }

            AssignChannel(prog, c.Id, c.Name, targetMode);
            await RespondAsync($"Assigned #{c.Name} to **{targetMode}**.").ConfigureAwait(false);
            return;
        }

        var added = cord.Settings.ChannelWhitelist.AddIfNew(reference);
        await RespondAsync(added ? $"Assigned #{c.Name}." : $"#{c.Name} is already assigned.").ConfigureAwait(false);
    }

    private const string MoveChannelId = "owner-move-channel";
    private const string LeaveId = "owner-leave";
    private const string ShutdownId = "owner-shutdown";
    private const string CancelId = "owner-cancel";

    /// <summary>Replaces the confirmation prompt with <paramref name="content"/> and removes its buttons.</summary>
    private Task ReplacePrompt(string content) => ((SocketMessageComponent)Context.Interaction).UpdateAsync(m =>
    {
        m.Content = content;
        m.Components = new ComponentBuilder().Build();
    });

    [ComponentInteraction(CancelId, ignoreGroupNames: true)]
    public Task CancelAction() => ReplacePrompt("Cancelled. Nothing was changed.");

    [ComponentInteraction($"{MoveChannelId}:*:*", ignoreGroupNames: true)]
    public async Task ConfirmMoveChannel(string channelId, string game)
    {
        if (cord.ProgramConfig is not { } prog || !ulong.TryParse(channelId, out var id) || !int.TryParse(game, out var mode))
        {
            await RespondAsync("That request is no longer valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var targetMode = (GameMode)mode;
        var name = Context.Channel.Id == id ? Context.Channel.Name : channelId;
        var movedFrom = AssignChannel(prog, id, name, targetMode);

        var msg = movedFrom.Count > 0
            ? $"Moved #{name} from **{string.Join(", ", movedFrom)}** to **{targetMode}**."
            : $"Assigned #{name} to **{targetMode}**.";
        await ReplacePrompt(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Assigns the channel to <paramref name="targetMode"/>, removing it from any other game's whitelist.
    /// </summary>
    /// <returns>The games the channel was removed from.</returns>
    private List<GameMode> AssignChannel(ProgramConfig prog, ulong channelId, string channelName, GameMode targetMode)
    {
        var movedFrom = new List<GameMode>();
        foreach (var (otherMode, settings) in prog.GameHubs)
        {
            if (otherMode != targetMode && settings.ChannelWhitelist.RemoveAll(z => z.ID == channelId) > 0)
                movedFrom.Add(otherMode);
        }

        prog.GetGameHubSettings(targetMode).ChannelWhitelist.AddIfNew(GetReference(channelId, channelName));
        return movedFrom;
    }

    [SlashCommand("remove-channel", "Removes this channel from the command whitelist.")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task RemoveChannel()
    {
        var channelId = Context.Interaction.Channel.Id;
        if (cord.ProgramConfig is { } prog)
        {
            var removedFrom = new List<GameMode>();
            foreach (var (mode, settings) in prog.GameHubs)
            {
                if (settings.ChannelWhitelist.RemoveAll(z => z.ID == channelId) > 0)
                    removedFrom.Add(mode);
            }

            var msg = removedFrom.Count > 0
                ? $"Removed channel from: **{string.Join(", ", removedFrom)}**."
                : "This channel was not assigned to any game.";
            await RespondAsync(msg).ConfigureAwait(false);
            return;
        }

        cord.Settings.ChannelWhitelist.RemoveAll(z => z.ID == channelId);
        await RespondAsync("Done.").ConfigureAwait(false);
    }

    [SlashCommand("leave", "Leaves this server, a specific server, or every server the bot is in.")]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.PrivateChannel)]
    public async Task Leave(
        [Summary(nameof(guildId), "ID of the server to leave. Leave blank for this server.")] string? guildId = null,
        [Summary(nameof(all), "Leave every server the bot is in.")] bool all = false)
    {
        string target;
        string description;
        if (all)
        {
            target = "all";
            description = $"all {Context.Client.Guilds.Count} servers";
        }
        else if (guildId is not null)
        {
            if (!ulong.TryParse(guildId, out var id) || Context.Client.GetGuild(id) is not { } guild)
            {
                await RespondAsync($"Provided input ({guildId}) is not a valid guild ID or the bot is not in the specified guild.", ephemeral: true).ConfigureAwait(false);
                return;
            }
            target = guild.Id.ToString();
            description = guild.Name;
        }
        else if (Context.Guild is { } current)
        {
            target = current.Id.ToString();
            description = current.Name;
        }
        else
        {
            await RespondAsync("Specify a guild ID when using this command outside of a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var buttons = new ComponentBuilder()
            .WithButton("Leave", $"{LeaveId}:{target}", ButtonStyle.Danger)
            .WithButton("Cancel", CancelId, ButtonStyle.Secondary)
            .Build();
        await RespondAsync($"Leave **{description}**? The bot will need to be re-invited to rejoin.", components: buttons, ephemeral: true).ConfigureAwait(false);
    }

    [ComponentInteraction($"{LeaveId}:*", ignoreGroupNames: true)]
    public async Task ConfirmLeave(string target)
    {
        List<SocketGuild> guilds = [];
        if (target == "all")
            guilds.AddRange(Context.Client.Guilds);
        else if (ulong.TryParse(target, out var id) && Context.Client.GetGuild(id) is { } guild)
            guilds.Add(guild);

        if (guilds.Count == 0)
        {
            await RespondAsync("The bot is no longer in that server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await ReplacePrompt(guilds.Count == 1 ? $"Leaving {guilds[0].Name}." : $"Leaving {guilds.Count} servers.").ConfigureAwait(false);
        foreach (var g in guilds)
            await g.LeaveAsync().ConfigureAwait(false);
    }

    [SlashCommand("shutdown", "Causes the entire process to end itself.")]
    public async Task ExitProgram()
    {
        var buttons = new ComponentBuilder()
            .WithButton("Shut down", ShutdownId, ButtonStyle.Danger)
            .WithButton("Cancel", CancelId, ButtonStyle.Secondary)
            .Build();
        await RespondAsync("Shut down the bot? It will stay offline until it is started again manually.", components: buttons, ephemeral: true).ConfigureAwait(false);
    }

    [ComponentInteraction(ShutdownId, ignoreGroupNames: true)]
    public async Task ConfirmShutdown()
    {
        await ReplacePrompt($"Shutting down... goodbye! {Format.Bold("Bot services are going offline.")}").ConfigureAwait(false);
        Environment.Exit(0);
    }

    [SlashCommand("check", "Checks if the bot has the required permissions in whitelisted channels.")]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.PrivateChannel)]
    public async Task ChannelPermissionTest()
    {
        List<GuildPermissionScanResult> results = [];
        foreach (var guild in Context.Client.Guilds)
        {
            var result = new GuildPermissionScanResult { GuildName = guild.Name };
            foreach (var channel in guild.TextChannels)
            {
                var whitelisted = cord.ProgramConfig is { } prog
                    ? prog.GameHubs.Values.Any(g => g.ChannelWhitelist.Contains(channel.Id))
                    : cord.Settings.ChannelWhitelist.Contains(channel.Id);
                if (!whitelisted)
                    continue;

                var missingPerms = GetMissingPerms(guild, channel);
                if (missingPerms.Count == 0)
                    continue;

                var c = new GuildChannelPermissionCheck { Channel = channel.Name };
                c.MissingPermissions.AddRange(missingPerms.Select(p => p.ToString()));
                result.InvalidChannels.Add(c);
            }

            if (result.InvalidChannels.Count != 0)
                results.Add(result);
        }

        if (results.Count == 0)
        {
            await RespondAsync("All permissions for whitelisted channels are correct.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var builder = new EmbedBuilder { Title = "Guilds with Missing Permissions", Color = Color.Red };
        foreach (var guild in results)
        {
            var fieldValue = string.Join("\n", guild.InvalidChannels.Select(c =>
                $"{c.Channel}: {string.Join(", ", c.MissingPermissions)}"));
            builder.AddField(guild.GuildName, fieldValue);
        }
        await RespondAsync(ephemeral: true, embed: builder.Build()).ConfigureAwait(false);
    }

    private static ReadOnlySpan<ChannelPermission> RequiredPermissions =>
    [
        ChannelPermission.ViewChannel,
        ChannelPermission.SendMessages,
        ChannelPermission.EmbedLinks,
        ChannelPermission.AttachFiles,
        ChannelPermission.ReadMessageHistory,
    ];

    private static List<ChannelPermission> GetMissingPerms(SocketGuild guild, SocketTextChannel channel)
    {
        List<ChannelPermission> result = [];
        var botPermissions = guild.CurrentUser.GetPermissions(channel);
        foreach (var perm in RequiredPermissions)
        {
            if (!botPermissions.Has(perm))
                result.Add(perm);
        }
        return result;
    }

    private sealed class GuildPermissionScanResult
    {
        public required string GuildName { get; init; }
        public List<GuildChannelPermissionCheck> InvalidChannels { get; } = [];
    }

    private sealed class GuildChannelPermissionCheck
    {
        public required string Channel { get; init; }
        public List<string> MissingPermissions { get; } = [];
    }

    private RemoteControlAccess GetReference(ulong id, string name) => new()
    {
        ID = id,
        Name = name,
        Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
    };
}
