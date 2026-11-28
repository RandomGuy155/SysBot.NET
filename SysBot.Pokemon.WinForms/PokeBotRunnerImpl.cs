using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Forms;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;
using SysBot.Base;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Twitch;
using SysBot.Pokemon.YouTube;

namespace SysBot.Pokemon.WinForms;

public interface ISharedDiscordRunner
{
    void RegisterWithDiscordHost(SysCordHost host);
}

internal static class SharedIntegrationChannels
{
    public static readonly ConcurrentDictionary<string, GameMode> Twitch = new(StringComparer.OrdinalIgnoreCase);
    public static readonly ConcurrentDictionary<string, GameMode> YouTube = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Bot Environment implementation with Integrations added.
/// </summary>
public class PokeBotRunnerImpl<T> : PokeBotRunner<T>, ISharedDiscordRunner where T : PKM, new()
{
    public PokeBotRunnerImpl(PokeTradeHub<T> hub, BotFactory<T> fac) : base(hub, fac) { }
    public PokeBotRunnerImpl(PokeTradeHubConfig config, BotFactory<T> fac) : base(config, fac) { }

    private TwitchBot<T>? Twitch;
    private YouTubeBot<T>? YouTube;
    public required Form Owner { get; init; }

    public void RegisterWithDiscordHost(SysCordHost host)
    {
        host.RegisterRunner(this);
        AddSpriteGenerating();
    }

    protected override void AddIntegrations()
    {
        AddSpriteGenerating();
        AddTwitchBot(Hub.Config.Twitch);
        AddYouTubeBot(Hub.Config.YouTube);
    }

    private void AddTwitchBot(TwitchSettings config)
    {
        if (Twitch != null)
            return; // already created
        if (string.IsNullOrWhiteSpace(config.Token) || string.IsNullOrWhiteSpace(config.Channel) || string.IsNullOrWhiteSpace(config.Username))
            return;

        if (!SharedIntegrationChannels.Twitch.TryAdd(config.Channel, Mode))
        {
            var owner = SharedIntegrationChannels.Twitch[config.Channel];
            LogUtil.LogInfo($"Skipping duplicate Twitch connection for #{config.Channel} ({Mode}); already connected for {owner}.");
            return;
        }

        Twitch = new TwitchBot<T>(Hub.Config.Twitch, Hub);
        if (Hub.Config.Twitch.DistributionCountDown)
            Hub.BotSync.BarrierReleasingActions.Add(() => Twitch.StartingDistribution(config.MessageStart));
    }

    private void AddYouTubeBot(YouTubeSettings config)
    {
        if (YouTube != null)
            return; // already created
        if (string.IsNullOrWhiteSpace(config.ClientID) || string.IsNullOrWhiteSpace(config.ChannelID) || string.IsNullOrWhiteSpace(config.ClientSecret))
            return;

        if (!SharedIntegrationChannels.YouTube.TryAdd(config.ChannelID, Mode))
        {
            var owner = SharedIntegrationChannels.YouTube[config.ChannelID];
            LogUtil.LogInfo($"Skipping duplicate YouTube connection for {config.ChannelID} ({Mode}); already connected for {owner}.");
            return;
        }

        Owner.Alert("Please log in with your web browser.");
        YouTube = new YouTubeBot<T>(Hub.Config.YouTube, Hub);
        Hub.BotSync.BarrierReleasingActions.Add(() => YouTube.StartingDistribution(config.MessageStart));
    }

    private static void AddSpriteGenerating()
    {
        SpriteName.AllowShinySprite = true;
        ReusableActions.GetSprite = pk =>
        {
            var img = pk.Sprite();
            var ms = new MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms;
        };
    }
}
