using System;
using System.Threading;

namespace SysBot.Pokemon.Discord;

public static class SysCordSettings
{
    private static readonly AsyncLocal<SysCordContext?> _current = new();
    private static SysCordContext? _fallback;

    public static SysCordContext? Current
    {
        get => _current.Value ?? _fallback;
        internal set
        {
            _current.Value = value;
            if (value is not null)
                _fallback = value;
        }
    }

    public static DiscordManager Manager => Current!.Manager;
    public static DiscordSettings Settings => Current!.Settings;
    public static PokeTradeHubConfig HubConfig => Current!.HubConfig;
    public static IServiceProvider ServiceProvider => Current!.ServiceProvider;

    public static int RegisteredCommands => Current?.RegisteredCommands ?? 0;
    public static int RegisteredModals => Current?.RegisteredModals ?? 0;
    public static DateTime RegisteredTime => Current?.RegisteredTime ?? default;

    public static void SetCommandsRegistered(int countCommand, int countModal)
        => Current?.SetCommandsRegistered(countCommand, countModal);
}
