using System.Text.Json;
using FluentAssertions;
using SysBot.Base;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

public class MultiGameConfigTests
{
    [Fact]
    public void ProgramConfig_JsonRoundTrip_SingleBotChannelRouting()
    {
        var cfg = new ProgramConfig
        {
            Mode = ProgramMode.LZA,
            Bots =
            [
                new PokeBotState
                {
                    Connection = new SwitchConnectionConfig { IP = "192.168.0.10", Port = 6000 },
                    InitialRoutine = PokeRoutineType.FlexTrade,
                    Mode = ProgramMode.SV,
                },
                new PokeBotState
                {
                    Connection = new SwitchConnectionConfig { IP = "192.168.0.11", Port = 6000 },
                    InitialRoutine = PokeRoutineType.FlexTrade,
                    Mode = null, // legacy bot without explicit Mode
                },
            ],
        };

        // Shared single Discord bot credentials
        cfg.Hub.Discord.Token = "shared-bot-token";
        cfg.Hub.Discord.SlashCommandHash = "HASH_SHARED";

        // Per-game channel routing & operational settings
        var lza = cfg.GetGameHubSettings(ProgramMode.LZA);
        lza.HelloResponse = "Hello LZA {0}!";
        lza.ChannelWhitelist.AddIfNew(new RemoteControlAccess { ID = 1001, Name = "lza-trades" });
        lza.Queues.CanQueue = true;
        lza.Timings.ExtraTimeConnectOnline = 1500;
        lza.Distribution.TradeCode = 1111;

        var sv = cfg.GetGameHubSettings(ProgramMode.SV);
        sv.HelloResponse = "Hello SV {0}!";
        sv.ChannelWhitelist.AddIfNew(new RemoteControlAccess { ID = 2002, Name = "sv-trades" });
        sv.Queues.CanQueue = false;
        sv.Timings.ExtraTimeConnectOnline = 2500;
        sv.Distribution.TradeCode = 2222;

        cfg.Hub.Discord.UserBlacklist.AddIfNew(new RemoteControlAccess { ID = 12345, Name = "BadUser" });
        cfg.Hub.TradeAbuse.BannedIDs.AddIfNew(new RemoteControlAccess { ID = 99999, Name = "BannedNID" });
        cfg.EnsurePrimaryHubSynced();

        var json = JsonSerializer.Serialize(cfg, ProgramConfigContext.Default.ProgramConfig);
        var loaded = JsonSerializer.Deserialize(json, ProgramConfigContext.Default.ProgramConfig);

        loaded.Should().NotBeNull();
        loaded!.EnsurePrimaryHubSynced();

        // Per-bot Mode & null fallback
        loaded.Bots.Should().HaveCount(2);
        loaded.Bots[0].Mode.Should().Be(ProgramMode.SV);
        loaded.GetEffectiveMode(loaded.Bots[0]).Should().Be(ProgramMode.SV);
        loaded.Bots[1].Mode.Should().BeNull();
        loaded.GetEffectiveMode(loaded.Bots[1]).Should().Be(ProgramMode.LZA);

        // Channel routing resolution
        var active = new[] { ProgramMode.LZA, ProgramMode.SV };
        loaded.ResolveModeForChannel(1001, active).Should().Be(ProgramMode.LZA);
        loaded.ResolveModeForChannel(2002, active).Should().Be(ProgramMode.SV);
        loaded.ResolveModeForChannel(9999, active).Should().BeNull();

        // Build per-game hub configs and verify shared vs isolated settings
        var lzaHub = loaded.BuildHubConfig(ProgramMode.LZA);
        var svHub = loaded.BuildHubConfig(ProgramMode.SV);

        lzaHub.Mode.Should().Be(ProgramMode.LZA);
        svHub.Mode.Should().Be(ProgramMode.SV);

        lzaHub.Discord.Token.Should().Be("shared-bot-token");
        svHub.Discord.Token.Should().Be("shared-bot-token");
        lzaHub.Discord.SlashCommandHash.Should().Be("HASH_SHARED");
        svHub.Discord.SlashCommandHash.Should().Be("HASH_SHARED");

        lzaHub.Discord.HelloResponse.Should().Be("Hello LZA {0}!");
        svHub.Discord.HelloResponse.Should().Be("Hello SV {0}!");

        lzaHub.Discord.ChannelWhitelist.Contains(1001).Should().BeTrue();
        lzaHub.Discord.ChannelWhitelist.Contains(2002).Should().BeFalse();
        svHub.Discord.ChannelWhitelist.Contains(2002).Should().BeTrue();
        svHub.Discord.ChannelWhitelist.Contains(1001).Should().BeFalse();

        lzaHub.Queues.CanQueue.Should().BeTrue();
        svHub.Queues.CanQueue.Should().BeFalse();

        lzaHub.Timings.ExtraTimeConnectOnline.Should().Be(1500);
        svHub.Timings.ExtraTimeConnectOnline.Should().Be(2500);

        lzaHub.Distribution.TradeCode.Should().Be(1111);
        svHub.Distribution.TradeCode.Should().Be(2222);

        // Shared objects are identical by reference across games
        lzaHub.Folder.Should().BeSameAs(svHub.Folder);
        lzaHub.TradeAbuse.Should().BeSameAs(svHub.TradeAbuse);
        lzaHub.Legality.Should().BeSameAs(svHub.Legality);
        lzaHub.Trade.Should().BeSameAs(svHub.Trade);
        lzaHub.Discord.UserBlacklist.Should().BeSameAs(svHub.Discord.UserBlacklist);
        lzaHub.Discord.GlobalSudoList.Should().BeSameAs(svHub.Discord.GlobalSudoList);
        lzaHub.Discord.RoleCanTrade.Should().BeSameAs(svHub.Discord.RoleCanTrade);

        // Runtime mutation on shared list is immediately visible on both hubs
        lzaHub.Discord.UserBlacklist.AddIfNew(new RemoteControlAccess { ID = 54321, Name = "NewBadUser" });
        svHub.Discord.UserBlacklist.Contains(54321).Should().BeTrue();

        // Runtime mutation on per-game queue state does NOT leak to the other game
        lzaHub.Queues.CanQueue = false;
        svHub.Queues.CanQueue = true;
        lzaHub.Queues.CanQueue.Should().BeFalse();
        svHub.Queues.CanQueue.Should().BeTrue();
    }

    [Fact]
    public void ProgramConfig_SingleActiveGameEmptyWhitelist_AllowsAnyChannel()
    {
        var cfg = new ProgramConfig { Mode = ProgramMode.SV };
        cfg.EnsurePrimaryHubSynced();

        var active = new[] { ProgramMode.SV };
        cfg.ResolveModeForChannel(777777, active).Should().Be(ProgramMode.SV);
    }

    [Fact]
    public void ProgramConfig_LegacyMigration_CopiesHubIntoPrimaryGameHub()
    {
        var legacy = new ProgramConfig { Mode = ProgramMode.SV };
        legacy.Hub.Discord.HelloResponse = "Legacy Hi {0}";
        legacy.Hub.Discord.ChannelWhitelist.AddIfNew(new RemoteControlAccess { ID = 5555, Name = "legacy-ch" });
        legacy.Hub.Distribution.TradeCode = 4321;

        legacy.GameHubs.Should().BeEmpty();
        legacy.EnsurePrimaryHubSynced();

        legacy.GameHubs.Should().ContainKey(ProgramMode.SV);
        legacy.GameHubs[ProgramMode.SV].HelloResponse.Should().Be("Legacy Hi {0}");
        legacy.GameHubs[ProgramMode.SV].ChannelWhitelist.Contains(5555).Should().BeTrue();
        legacy.GameHubs[ProgramMode.SV].Distribution.TradeCode.Should().Be(4321);

        // Updating GameHubs[Mode] mirrors back to Hub on EnsurePrimaryHubSynced()
        legacy.GameHubs[ProgramMode.SV].HelloResponse = "Updated Hi {0}";
        legacy.EnsurePrimaryHubSynced();
        legacy.Hub.Discord.HelloResponse.Should().Be("Updated Hi {0}");
    }
}
