using System;
using Discord.WebSocket;

namespace SysBot.Pokemon.Discord;

/// <summary>
/// Instance-scoped context for a single <see cref="SysCord{T}"/> Discord bot.
/// </summary>
public sealed class SysCordContext(DiscordManager manager, PokeTradeHubConfig hubConfig, DiscordSocketClient client, ProgramConfig? programConfig = null)
{
    public DiscordManager Manager { get; } = manager;
    public DiscordSettings Settings => Manager.Config;
    public PokeTradeHubConfig HubConfig { get; } = hubConfig;
    public DiscordSocketClient Client { get; } = client;
    public ProgramConfig? ProgramConfig { get; } = programConfig;
    public IServiceProvider ServiceProvider { get; internal set; } = null!;

    public int RegisteredCommands { get; private set; }
    public int RegisteredModals { get; private set; }
    public DateTime RegisteredTime { get; private set; }

    public void SetCommandsRegistered(int countCommand, int countModal)
    {
        RegisteredCommands = countCommand;
        RegisteredModals = countModal;
        RegisteredTime = DateTime.UtcNow;
    }
}

