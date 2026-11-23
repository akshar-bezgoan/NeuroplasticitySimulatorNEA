using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Neuroplasticity.Core;
using Neuroplasticity.Data;

namespace Neuroplasticity.UI;

/// <summary>Application shell: sidebar navigation, the screens, and the clock that runs every live simulation.</summary>
public class MainWindow : Window
{
    public readonly UserRecord User;
    public readonly List<SimulationSession> Live = new();
    public Database Db => App.Db;
    public SimulationSession Current { get; private set; }

    readonly ContentControl host = new();
    readonly Grid shell = new() { ColumnDefinitions = new ColumnDefinitions("230,*") };
    readonly Border sideFrame = new() { CornerRadius = new CornerRadius(UiTheme.Radius), Margin = new Thickness(10), ClipToBounds = true };
    readonly Button sidebarToggle = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(18, 12), Padding = new Thickness(12, 7), CornerRadius = new CornerRadius(UiTheme.Radius) };
    bool sidebarVisible = true;
    readonly TextBlock banner = new() { Foreground = UiTheme.B(UiTheme.Soft), Margin = new Thickness(16, 6), FontSize = 13 };
    readonly Dictionary<string, Button> nav = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch clock = Stopwatch.StartNew();
    long lastMs;

    public MainWindow(UserRecord u)
    {
        User = u; Title = "Neuroplasticity Simulator"; Width = 1360; Height = 900; MinWidth = 1000; MinHeight = 700; FontFamily = new FontFamily("Avenir Next");
        Background = UiTheme.B(UiTheme.Bg); WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var top = new StackPanel { Margin = new Thickness(8, 16) };
        top.Children.Add(new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 8, 8, 24), Children = {
            UiTheme.T("NEUROPLASTICITY", 11, true, UiTheme.SideMuted), UiTheme.T("SIMULATOR", 11, true, UiTheme.SideText) } });
        top.Children.Add(Nav("Home", "Home", ShowDashboard));
        top.Children.Add(Nav("Experiments", "My Experiments", ShowExperiments));
        top.Children.Add(Nav("New", "New Experiment", ShowNew));
        top.Children.Add(Nav("Simulation", "Simulation", () => { if (Current != null) OpenWorkspace(Current); }));
        top.Children.Add(Nav("Results", "Results", () => ShowResults()));
        top.Children.Add(Nav("Account", "Account Settings", ShowAccount));
        var logout = UiTheme.Btn("Log out", () => { new LoginWindow().Show(); Close(); }, fg: UiTheme.Text, outline: true);
        logout.Margin = new Thickness(8); logout.HorizontalAlignment = HorizontalAlignment.Stretch;
        var side = new DockPanel { Background = UiTheme.B(UiTheme.Side), ClipToBounds = true };
        DockPanel.SetDock(logout, Dock.Bottom); side.Children.Add(logout); side.Children.Add(top);
        sideFrame.Child = side;

        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        sidebarToggle.Content = IconLabel("menu", "Hide menu");
        sidebarToggle.Click += (_, _) => SetSidebar(!sidebarVisible);
        right.Children.Add(sidebarToggle); Grid.SetRow(host, 1); right.Children.Add(host); Grid.SetRow(banner, 2); right.Children.Add(banner);
        shell.Background = UiTheme.B(UiTheme.Bg);
        Grid.SetColumn(right, 1); shell.Children.Add(sideFrame); shell.Children.Add(right);
        Content = shell;

        ShowDashboard();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closing += (_, _) => { timer.Stop(); SaveAll(); };
    }

    Button Nav(string key, string text, Action go)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent, Foreground = UiTheme.B(UiTheme.SideText), Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(UiTheme.Radius), Margin = new Thickness(0, 2), FontFamily = new FontFamily("Avenir Next") };
        b.Click += (_, _) => go();
        nav[key] = b; return b;
    }

    void SetSidebar(bool visible)
    {
        sidebarVisible = visible;
        sideFrame.IsVisible = visible;
        shell.ColumnDefinitions[0].Width = visible ? new GridLength(230) : new GridLength(0);
        sidebarToggle.Content = IconLabel("menu", visible ? "Hide menu" : "Show menu");
    }

    static Control IconLabel(string icon, string label) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
        Children = { new LineIcon { Kind = icon, Width = 18, Height = 18, Stroke = UiTheme.B(UiTheme.SideText), VerticalAlignment = VerticalAlignment.Center }, UiTheme.T(label, 13, false, UiTheme.SideText) }
    };

    void Go(string key, Control view)
    {
        host.Content = view;
        foreach (var kv in nav)
        {
            bool selected = kv.Key == key;
            kv.Value.Background = selected ? UiTheme.B(UiTheme.SideActive) : Brushes.Transparent;
            kv.Value.Foreground = UiTheme.B(selected ? UiTheme.SideText : UiTheme.SideMuted);
        }
        nav["Simulation"].IsVisible = Current != null;
    }

    // ---------------- navigation ----------------
    public void ShowDashboard() => Go("Home", new DashboardView(this));
    public void ShowExperiments() => Go("Experiments", new ExperimentsView(this));
    public void ShowNew() => Go("New", new NewExperimentView(this));
    public void ShowResults(long? id = null) => Go("Results", new ResultsView(this, id));
    public void ShowAccount() => Go("Account", new AccountView(this));
    public void OpenWorkspace(SimulationSession s) { Current = s; Go("Simulation", new WorkspaceView(this, s)); }
    public void Rebuild() { (host.Content as IRefreshable)?.Rebuild(); nav["Simulation"].IsVisible = Current != null; }

    public void Notify(string text)
    {
        banner.Text = text;
        DispatcherTimer.RunOnce(() => { if (banner.Text == text) banner.Text = ""; }, TimeSpan.FromSeconds(7));
    }

    // ---------------- simulation lifecycle ----------------
    void Tick()
    {
        long now = clock.ElapsedMilliseconds, dt = now - lastMs; lastMs = now;
        var running = Live.Where(s => s.Running).ToList();
        foreach (var s in running)
        {
            s.Step(s.Speed, Math.Max(5, 25 / running.Count));
            s.Elapsed += TimeSpan.FromMilliseconds(dt);
            if (s.Finished) Complete(s);
        }
        (host.Content as IRefreshable)?.Refresh();
    }

    void Complete(SimulationSession s)
    {
        s.Running = false; s.Status = "Completed"; Save(s); Live.Remove(s);
        Notify($"“{s.Name}” finished all {s.TargetCycles:N0} cycles.");
        Rebuild();
    }

    public SimulationSession CreateSession(string name, string kind, string path, Dataset data, int target, DevelopmentStage stage, RuleKind rule)
    {
        var s = new SimulationSession { Name = name, DataKind = kind, DataPath = path, Data = data, TargetCycles = target };
        s.AddAgent(stage, rule);                  // a new simulation starts with ONE agent; more can be added in the workspace
        s.Running = true; s.Status = "Running";
        Live.Add(s); Save(s); OpenWorkspace(s);
        return s;
    }

    public void ToggleRun(SimulationSession s)
    {
        if (s.Finished) return;
        s.Running = !s.Running; s.Status = s.Running ? "Running" : "Paused";
        if (!s.Running) Save(s);
        Rebuild();
    }

    public void Stop(SimulationSession s)
    {
        s.Running = false; s.Status = "Completed"; Save(s); Live.Remove(s);
        if (Current == s) Current = null;
        Notify($"Stopped “{s.Name}”. Results are saved.");
        ShowDashboard();
    }

    /// <summary>Returns the live session for a saved experiment, loading its checkpoints if needed.</summary>
    public SimulationSession OpenSaved(ExperimentInfo info)
    {
        var live = Live.FirstOrDefault(x => x.DbId == info.Id);
        if (live != null) return live;
        try
        {
            var snaps = Db.LoadCheckpoints(info.Id, User.Id).Select(SimulationSerialiser.Deserialise).ToList();
            if (snaps.Count == 0) { Notify("That experiment has no saved checkpoint."); return null; }
            var data = Dataset.Load(info.DataKind, info.DataPath);
            var s = new SimulationSession { DbId = info.Id, Name = info.Name, Data = data, DataKind = info.DataKind, DataPath = info.DataPath,
                TargetCycles = info.TargetCycles, Elapsed = TimeSpan.FromSeconds(info.RuntimeSec), Status = info.Status == "Completed" ? "Completed" : "Paused" };
            foreach (var sn in snaps) s.Agents.Add(Agent.Restore(sn, data));
            Live.Add(s);
            return s;
        }
        catch (Exception ex) { Notify("Could not open experiment: " + ex.Message); return null; }
    }

    public void Resume(ExperimentInfo info)
    {
        var s = OpenSaved(info);
        if (s == null) return;
        if (s.Finished) { s.Status = "Completed"; Notify("That experiment already reached its target cycles."); return; }
        s.Running = true; s.Status = "Running";
        OpenWorkspace(s);
    }

    public void Delete(ExperimentInfo info)
    {
        var live = Live.FirstOrDefault(x => x.DbId == info.Id);
        if (live != null) { Live.Remove(live); if (Current == live) Current = null; }
        Db.DeleteExperiment(info.Id, User.Id);
        Notify($"Deleted “{info.Name}”.");
        Rebuild();
    }

    public void Save(SimulationSession s)
    {
        try { Db.SaveSession(User.Id, s); }
        catch (Exception ex) { Notify("Could not save: " + ex.Message); }
    }

    void SaveAll()
    {
        foreach (var s in Live.ToList()) { s.Running = false; if (s.Status == "Running") s.Status = "Paused"; Save(s); }
    }

    // ---------------- shared helpers for the screens ----------------
    public List<ChartSeries> SeriesFor(ExperimentInfo info)
    {
        var live = Live.FirstOrDefault(x => x.DbId == info.Id);
        if (live != null) return live.Agents.Select(a => new ChartSeries(a.Id, live.ColourOf(a), a.History, a.Events)).ToList();
        var list = new List<ChartSeries>();
        int i = 0;
        foreach (var b in Db.LoadCheckpoints(info.Id, User.Id))
        {
            var sn = SimulationSerialiser.Deserialise(b);
            list.Add(new ChartSeries(sn.AgentId, SimulationSession.Palette[i++ % SimulationSession.Palette.Length], sn.History, sn.Events));
        }
        return list;
    }

    public async Task ExportCsvAsync(ExperimentInfo info)
    {
        var live = Live.FirstOrDefault(x => x.DbId == info.Id);
        if (live != null) Save(live);   // make sure the file reflects the latest state
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export results", SuggestedFileName = $"{info.Name}.csv" });
        if (f?.TryGetLocalPath() is { } p) { File.WriteAllText(p, Db.ExportCsv(info.Id, User.Id)); Notify("Exported to " + p); }
    }
}
