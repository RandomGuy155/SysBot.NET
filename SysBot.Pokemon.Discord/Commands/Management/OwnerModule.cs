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
        cord.Settings.GlobalSudoList.AddIfNew(GetReference(user.Id, user.GlobalName));
        await RespondAsync("Done.").ConfigureAwait(false);
    }

    [SlashCommand("remove-sudo", "Removes a user from global sudo.")]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.PrivateChannel)]
    public async Task RemoveSudo(IUser user)
    {
        cord.Settings.GlobalSudoList.RemoveAll(z => z.ID == user.Id);
        await RespondAsync("Done.").ConfigureAwait(false);
    }

    [SlashCommand("add-channel", "Assigns this channel to a game's command whitelist.")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task AddChannel(
        [Summary(nameof(game), "Game to route commands from this channel to. Leave blank for the current game.")]
        [Choice("SWSH", (int)ProgramMode.SWSH)]
        [Choice("BDSP", (int)ProgramMode.BDSP)]
        [Choice("LA", (int)ProgramMode.LA)]
        [Choice("SV", (int)ProgramMode.SV)]
        [Choice("LZA", (int)ProgramMode.LZA)]
        int? game = null)
    {
        var c = Context.Interaction.Channel;
        var reference = GetReference(c.Id, c.Name);

        if (cord.ProgramConfig is { } prog)
        {
            var targetMode = game is { } g && g != (int)ProgramMode.None
                ? (ProgramMode)g
                : (cord.HubConfig.Mode != ProgramMode.None ? cord.HubConfig.Mode : prog.Mode);

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
                    .WithButton("Cancel", CancelMoveChannelId, ButtonStyle.Secondary)
                    .Build();
                await RespondAsync(prompt, components: buttons, ephemeral: true).ConfigureAwait(false);
                return;
            }

            AssignChannel(prog, c.Id, c.Name, targetMode);
            await RespondAsync($"Assigned #{c.Name} to **{targetMode}**.").ConfigureAwait(false);
            return;
        }

        cord.Settings.ChannelWhitelist.AddIfNew(reference);
        await RespondAsync("Done.").ConfigureAwait(false);
    }

    private const string MoveChannelId = "owner-move-channel";
    private const string CancelMoveChannelId = "owner-move-channel-cancel";

    [ComponentInteraction($"{MoveChannelId}:*:*", ignoreGroupNames: true)]
    public async Task ConfirmMoveChannel(string channelId, string game)
    {
        if (cord.ProgramConfig is not { } prog || !ulong.TryParse(channelId, out var id) || !int.TryParse(game, out var mode))
        {
            await RespondAsync("That request is no longer valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var targetMode = (ProgramMode)mode;
        var name = Context.Channel.Id == id ? Context.Channel.Name : channelId;
        var movedFrom = AssignChannel(prog, id, name, targetMode);

        var msg = movedFrom.Count > 0
            ? $"Moved #{name} from **{string.Join(", ", movedFrom)}** to **{targetMode}**."
            : $"Assigned #{name} to **{targetMode}**.";
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(m =>
        {
            m.Content = msg;
            m.Components = new ComponentBuilder().Build();
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(CancelMoveChannelId, ignoreGroupNames: true)]
    public async Task CancelMoveChannel()
    {
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(m =>
        {
            m.Content = "Cancelled. The channel assignment was not changed.";
            m.Components = new ComponentBuilder().Build();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Assigns the channel to <paramref name="targetMode"/>, removing it from any other game's whitelist.
    /// </summary>
    /// <returns>The games the channel was removed from.</returns>
    private List<ProgramMode> AssignChannel(ProgramConfig prog, ulong channelId, string channelName, ProgramMode targetMode)
    {
        var movedFrom = new List<ProgramMode>();
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
            var removedFrom = new List<ProgramMode>();
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

    [SlashCommand("leave", "Leaves the current server.")]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task Leave()
    {
        await RespondAsync("Goodbye.").ConfigureAwait(false);
        if (Context.Guild is not null) await Context.Guild.LeaveAsync().ConfigureAwait(false);
    }

    [SlashCommand("leave-guild", "Leaves a guild by ID.")]
    public async Task LeaveGuild(string guildId)
    {
        if (!ulong.TryParse(guildId, out var id))
        {
            await RespondAsync("Please provide a valid Guild ID.").ConfigureAwait(false);
            return;
        }

        var guild = Context.Client.Guilds.FirstOrDefault(x => x.Id == id);
        if (guild is null)
        {
            await RespondAsync($"Provided input ({guildId}) is not a valid guild ID or the bot is not in the specified guild.").ConfigureAwait(false);
            return;
        }

        await RespondAsync($"Leaving {guild}.").ConfigureAwait(false);
        await guild.LeaveAsync().ConfigureAwait(false);
    }

    [SlashCommand("leave-all", "Leaves all servers the bot is currently in.")]
    public async Task LeaveAll()
    {
        await RespondAsync("Leaving all servers.").ConfigureAwait(false);
        foreach (var guild in Context.Client.Guilds) await guild.LeaveAsync().ConfigureAwait(false);
    }

    [SlashCommand("shutdown", "Causes the entire process to end itself.")]
    public async Task ExitProgram()
    {
        var emphasis = Format.Bold("Bot services are going offline.");
        await RespondAsync($"Shutting down... goodbye! {emphasis}").ConfigureAwait(false);
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
