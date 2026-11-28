using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord;

namespace SysBot.Pokemon.WinForms;

public sealed partial class Main : Form
{
    private readonly List<PokeBotState> Bots = [];
    private readonly Dictionary<GameMode, IPokeBotRunner> Runners = [];
    private readonly ConcurrentDictionary<string, GameMode> BotModeLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProgramConfig Config = Program.Config;
    private readonly string BaseTitle;
    private TextBoxForwarder? LogForwarder;
    private SysCordHost? DiscordHost;

    private static readonly GameMode[] SupportedModes =
        Enum.GetValues<GameMode>().Where(m => m != GameMode.None).ToArray();

    public Main()
    {
        InitializeComponent();
        BaseTitle = Text;

        LogUtil.GameTagResolver = ResolveGameTag;

        RTB_Logs.MaxLength = 32_767; // character length
        LoadControls();

        foreach (var bot in Config.Bots)
        {
            bot.Initialize();
            AddBot(bot);
        }

        UpdateWindowTitle();
        Task.Run(BotMonitor);

        var trainer = Config.Hub.Legality;
        InitUtil.InitializeStubs(Config.Mode, trainer.GenerateOT, trainer.GenerateLanguage);

        if (Application.IsDarkModeEnabled)
        {
            foreach (var control in this.GetChildrenOfType<Control>())
                WinFormsUtil.ReformatDark(control);
        }

        if (Config is not { Width: 0, Height: 0 })
        {
            Width = Config.Width;
            Height = Config.Height;
        }

        B_New.Height = CB_Protocol.Height;
        FLP_BotCreator.Height = B_New.Height + B_New.Margin.Vertical;
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        TC_Main.ItemSize = new((int)(TC_Main.ItemSize.Width * factor.Width), (int)(TC_Main.ItemSize.Height * factor.Height));
        TC_Hub.ItemSize = new((int)(TC_Hub.ItemSize.Width * factor.Height), (int)(TC_Hub.ItemSize.Height * factor.Width));
        TC_Logs.ItemSize = new((int)(TC_Logs.ItemSize.Width * factor.Height), (int)(TC_Logs.ItemSize.Height * factor.Width));
    }

    private GameMode? ResolveBotMode(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return null;
        if (BotModeLookup.TryGetValue(identity, out var mode))
            return mode;

        // A bot's Connection.Label changes after trainer identification (e.g. IP -> "SysBot-123456").
        try
        {
            foreach (var (runnerMode, runner) in Runners)
            {
                if (runner is not BotRunner<PokeBotState> br)
                    continue;
                foreach (var src in br.Bots)
                {
                    var conn = src.Bot.Connection;
                    if (string.Equals(conn.Label, identity, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(conn.Name, identity, StringComparison.OrdinalIgnoreCase))
                    {
                        BotModeLookup[identity] = runnerMode;
                        return runnerMode;
                    }
                }
            }
        }
        catch
        {
            // Ignore transient collection modification while bots are being added/removed.
        }
        return null;
    }

    private string? ResolveGameTag(string identity) => ResolveBotMode(identity)?.ToString();

    private IPokeBotRunner GetOrCreateRunner(GameMode mode)
    {
        if (Runners.TryGetValue(mode, out var existing))
            return existing;

        var hubCfg = Config.BuildHubConfig(mode);
        IPokeBotRunner runner = mode switch
        {
            GameMode.SWSH => new PokeBotRunnerImpl<PK8>(hubCfg, new BotFactory8SWSH()) { Owner = this },
            GameMode.BDSP => new PokeBotRunnerImpl<PB8>(hubCfg, new BotFactory8BS()) { Owner = this },
            GameMode.LA   => new PokeBotRunnerImpl<PA8>(hubCfg, new BotFactory8LA()) { Owner = this },
            GameMode.SV   => new PokeBotRunnerImpl<PK9>(hubCfg, new BotFactory9SV()) { Owner = this },
            GameMode.LZA  => new PokeBotRunnerImpl<PA9>(hubCfg, new BotFactory9LZA()) { Owner = this },
            _ => throw new IndexOutOfRangeException("Unsupported mode."),
        };

        Runners[mode] = runner;
        if (DiscordHost is not null && runner is ISharedDiscordRunner shared)
            shared.RegisterWithDiscordHost(DiscordHost);
        return runner;
    }

    private void EnsureSharedDiscordStarted()
    {
        if (DiscordHost is not null)
            return;

        var token = Config.Hub.Discord.Token;
        if (string.IsNullOrWhiteSpace(token))
            return;

        var host = new SysCordHost(Config);
        foreach (var runner in Runners.Values)
        {
            if (runner is ISharedDiscordRunner shared)
                shared.RegisterWithDiscordHost(host);
        }

        DiscordHost = host;
        Task.Run(() => host.MainAsync(token, CancellationToken.None));
    }

    private static readonly Dictionary<GameMode, Func<PokeRoutineType, bool>> RoutineSupport = new()
    {
        [GameMode.SWSH] = new BotFactory8SWSH().SupportsRoutine,
        [GameMode.BDSP] = new BotFactory8BS().SupportsRoutine,
        [GameMode.LA]   = new BotFactory8LA().SupportsRoutine,
        [GameMode.SV]   = new BotFactory9SV().SupportsRoutine,
        [GameMode.LZA]  = new BotFactory9LZA().SupportsRoutine,
    };

    private static bool SupportsRoutine(GameMode mode, PokeRoutineType routine)
        => RoutineSupport.TryGetValue(mode, out var check) && check(routine);

    private void UpdateWindowTitle()
    {
        var active = Runners.Keys.Order().ToArray();
        var suffix = active.Length > 0 ? string.Join(", ", active) : Config.Mode.ToString();
        Text = $"{BaseTitle} ({suffix})";
    }

    private async Task BotMonitor()
    {
        while (!Disposing)
        {
            try
            {
                foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    c.ReadState();
            }
            catch
            {
                // Updating the collection by adding/removing bots will change the iterator
                // Can try a for-loop or ToArray, but those still don't prevent concurrent mutations of the array.
                // Just try, and if failed, ignore. Next loop will be fine. Locks on the collection are kinda overkill, since this task is not critical.
            }
            await Task.Delay(2_000).ConfigureAwait(false);
        }
    }

    private void LoadControls()
    {
        var protocols = Enum.GetValues<SwitchProtocol>();
        var listP = protocols.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Protocol.DisplayMember = nameof(ComboItem.Text);
        CB_Protocol.ValueMember = nameof(ComboItem.Value);
        CB_Protocol.DataSource = listP;
        CB_Protocol.SelectedIndex = (int)SwitchProtocol.WiFi; // default option

        var games = SupportedModes.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Game.DisplayMember = nameof(ComboItem.Text);
        CB_Game.ValueMember = nameof(ComboItem.Value);
        CB_Game.DataSource = games;
        CB_Game.SelectedValue = (int)Config.Mode;

        PopulateRoutines((GameMode)WinFormsUtil.GetIndex(CB_Game));

        PopulateVerticalTabs(TC_Hub, "Shared", PG_Hub, []);
        PG_Hub.SelectedObject = new SharedHubView(Config);

        LogForwarder = new TextBoxForwarder(RTB_Logs, ResolveBotMode);
        LogUtil.Forwarders.Add(LogForwarder);

        PopulateVerticalTabs(TC_Logs, "All", RTB_Logs, []);
    }

    private GameMode[] GetActiveModes() =>
        SupportedModes.Where(mode =>
            Runners.ContainsKey(mode)
            || Bots.Any(b => Config.GetEffectiveMode(b) == mode)).ToArray();

    private void RefreshGameTabs()
    {
        var modes = GetActiveModes();
        if (PopulateVerticalTabs(TC_Hub, "Shared", PG_Hub, modes))
            TC_Hub_SelectedIndexChanged(TC_Hub, EventArgs.Empty);
        if (PopulateVerticalTabs(TC_Logs, "All", RTB_Logs, modes))
            TC_Logs_SelectedIndexChanged(TC_Logs, EventArgs.Empty);
    }

    private static bool PopulateVerticalTabs(TabControl tc, string defaultLabel, Control sharedContent, GameMode[] modes)
    {
        var expectedCount = 1 + modes.Length;
        if (tc.TabPages.Count == expectedCount)
        {
            bool same = tc.TabPages[0].Tag is 0;
            for (int i = 0; same && i < modes.Length; i++)
                same = tc.TabPages[i + 1].Tag is int tag && tag == (int)modes[i];
            if (same)
                return false;
        }

        var selectedTag = tc.SelectedTab?.Tag is int cur ? cur : 0;

        tc.SuspendLayout();
        sharedContent.Parent?.Controls.Remove(sharedContent);
        tc.TabPages.Clear();

        var first = new TabPage(defaultLabel) { Tag = 0, Padding = Padding.Empty, Margin = Padding.Empty };
        if (Application.IsDarkModeEnabled)
            WinFormsUtil.ReformatDark(first);
        tc.TabPages.Add(first);

        TabPage target = first;
        foreach (var mode in modes)
        {
            var page = new TabPage(mode.ToString()) { Tag = (int)mode, Padding = Padding.Empty, Margin = Padding.Empty };
            if (Application.IsDarkModeEnabled)
                WinFormsUtil.ReformatDark(page);
            tc.TabPages.Add(page);
            if ((int)mode == selectedTag)
                target = page;
        }

        target.Controls.Add(sharedContent);
        tc.SelectedTab = target;
        tc.ResumeLayout();
        return true;
    }

    private void PopulateRoutines(GameMode mode)
    {
        var previous = CB_Routine.SelectedValue is int v ? (PokeRoutineType)v : PokeRoutineType.FlexTrade;
        var routines = Enum.GetValues<PokeRoutineType>().Where(z => SupportsRoutine(mode, z)).ToArray();
        var list = routines.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Routine.DisplayMember = nameof(ComboItem.Text);
        CB_Routine.ValueMember = nameof(ComboItem.Value);
        CB_Routine.DataSource = list;

        if (routines.Contains(previous))
            CB_Routine.SelectedValue = (int)previous;
        else if (routines.Contains(PokeRoutineType.FlexTrade))
            CB_Routine.SelectedValue = (int)PokeRoutineType.FlexTrade;
        else if (list.Length > 0)
            CB_Routine.SelectedIndex = 0;
    }

    private void SyncAllRunnerConfigs()
    {
        foreach (var runner in Runners.Values)
            Config.SyncHubConfig(runner.Config);
    }

    private ProgramConfig GetCurrentConfiguration()
    {
        Config.Bots = [.. Bots];
        return Config;
    }

    private void Main_FormClosing(object sender, FormClosingEventArgs e)
    {
        SaveCurrentConfig();
        var running = Runners.Values.Where(r => r.IsRunning).ToArray();
        if (running.Length == 0)
            return;

        async Task WaitUntilNotRunning()
        {
            while (running.Any(r => r.IsRunning))
                await Task.Delay(10).ConfigureAwait(false);
        }

        // Try to let all bots hard-stop before ending execution of the entire program.
        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = false;
        foreach (var runner in running)
            runner.StopAll();
        Task.WhenAny(WaitUntilNotRunning(), Task.Delay(5_000)).ConfigureAwait(true).GetAwaiter().GetResult();
    }

    private void SaveCurrentConfig()
    {
        var cfg = GetCurrentConfiguration();
        cfg.Width = Width;
        cfg.Height = Height;
        ConfigLoader.Save(cfg);
    }

    private void B_Start_Click(object sender, EventArgs e)
    {
        SaveCurrentConfig();

        LogUtil.LogInfo("Starting all bots...");
        if (Runners.Count == 0)
        {
            GetOrCreateRunner(Config.Mode);
            RefreshGameTabs();
        }

        foreach (var runner in Runners.Values)
        {
            Config.SyncHubConfig(runner.Config);
            runner.InitializeStart();
        }

        EnsureSharedDiscordStarted();
        SendAll(BotControlCommand.Start);
        Tab_Logs.Select();

        if (Bots.Count == 0)
            this.Alert("No bots configured, but all supporting services have been started.");
    }

    private void SendAll(BotControlCommand cmd)
    {
        foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            c.SendCommand(cmd, false);

        EchoUtil.Echo($"All bots have been issued a command to {cmd}.");
    }

    private void B_Stop_Click(object sender, EventArgs e)
    {
        var anyRunning = Runners.Values.Any(r => r.IsRunning);
        if (!anyRunning && (ModifierKeys & Keys.Alt) == 0)
        {
            this.Alert("Nothing is currently running.");
            return;
        }

        var cmd = BotControlCommand.Stop;

        if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0) // either, because remembering which can be hard
        {
            if (anyRunning)
            {
                this.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                cmd = BotControlCommand.Idle;
            }
            else
            {
                this.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                cmd = BotControlCommand.Resume;
            }
        }
        SendAll(cmd);
    }

    private void B_New_Click(object sender, EventArgs e)
    {
        var cfg = CreateNewBotConfig();
        if (!AddBot(cfg))
        {
            this.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
            return;
        }
        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool AddBot(PokeBotState cfg)
    {
        if (!cfg.IsValid())
            return false;

        if (Bots.Any(z => z.Connection.Equals(cfg.Connection)))
            return false;

        var mode = Config.GetEffectiveMode(cfg);
        var runner = GetOrCreateRunner(mode);

        PokeRoutineExecutorBase newBot;
        try
        {
            newBot = runner.CreateBotFromConfig(cfg);
        }
        catch
        {
            Console.WriteLine($"Game Mode ({mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
            return false;
        }

        try
        {
            runner.Add(newBot);
        }
        catch (ArgumentException ex)
        {
            this.Error(ex.Message);
            return false;
        }

        BotModeLookup[newBot.Connection.Label] = mode;
        BotModeLookup[newBot.Connection.Name] = mode;

        AddBotControl(runner, cfg);
        Bots.Add(cfg);
        RefreshGameTabs();
        UpdateWindowTitle();
        return true;
    }

    private void AddBotControl(IPokeBotRunner runner, PokeBotState cfg)
    {
        var row = new BotController { Width = FLP_Bots.Width, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        row.Initialize(runner, cfg);
        row.BeforeStart = () =>
        {
            SyncAllRunnerConfigs();
            EnsureSharedDiscordStarted();
        };
        FLP_Bots.Controls.Add(row);
        FLP_Bots.SetFlowBreak(row, true);
        row.AddClickHandler(() =>
        {
            var details = cfg.Connection;
            TB_IP.Text = details.IP;
            NUD_Port.Text = details.Port.ToString();
            CB_Protocol.SelectedIndex = (int)details.Protocol;
            CB_Game.SelectedValue = (int)runner.Mode;
            CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
        });

        row.Remove += (s, e) =>
        {
            var botSource = runner.GetBot(row.State);
            if (botSource is not null)
            {
                BotModeLookup.TryRemove(botSource.Bot.Connection.Label, out _);
                BotModeLookup.TryRemove(botSource.Bot.Connection.Name, out _);
            }

            Bots.Remove(row.State);
            runner.Remove(row.State, !runner.Config.SkipConsoleBotCreation);
            FLP_Bots.Controls.Remove(row);

            if (!runner.IsRunning && !runner.RunOnce && !Bots.Any(b => Config.GetEffectiveMode(b) == runner.Mode))
                Runners.Remove(runner.Mode);

            RefreshGameTabs();
            UpdateWindowTitle();
        };
    }

    private PokeBotState CreateNewBotConfig()
    {
        var ip = TB_IP.Text;
        var port = int.TryParse(NUD_Port.Text, out var p) ? p : 6000;
        var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port);
        cfg.Protocol = (SwitchProtocol)WinFormsUtil.GetIndex(CB_Protocol);

        var mode = (GameMode)WinFormsUtil.GetIndex(CB_Game);
        var pk = new PokeBotState { Connection = cfg, Mode = mode };
        var type = (PokeRoutineType)WinFormsUtil.GetIndex(CB_Routine);
        pk.Initialize(type);
        return pk;
    }

    private void FLP_Bots_Resize(object sender, EventArgs e)
    {
        foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            c.Width = FLP_Bots.Width;
    }

    private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
    {
        var isWifi = CB_Protocol.SelectedIndex == 0;
        TB_IP.Visible = isWifi;
        NUD_Port.Visible = !isWifi;

        if (isWifi)
            NUD_Port.Text = "6000";
    }

    private void CB_Game_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (CB_Game.SelectedValue is not int val)
            return;

        PopulateRoutines((GameMode)val);
    }

    private void TC_Hub_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (TC_Hub.SelectedTab is not { Tag: int val } tab)
            return;

        if (!tab.Controls.Contains(PG_Hub))
            tab.Controls.Add(PG_Hub);

        PG_Hub.SelectedObject = val == 0
            ? new SharedHubView(Config)
            : GameHubView.Create((GameMode)val, Config);
    }

    private void TC_Logs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (TC_Logs.SelectedTab is not { Tag: int val } tab)
            return;

        if (!tab.Controls.Contains(RTB_Logs))
            tab.Controls.Add(RTB_Logs);

        LogForwarder?.SetFilter(val == 0 ? null : (GameMode)val);
    }

    private void PG_Hub_PropertyValueChanged(object? s, PropertyValueChangedEventArgs e)
    {
        SyncAllRunnerConfigs();
        UpdateWindowTitle();
    }
}
