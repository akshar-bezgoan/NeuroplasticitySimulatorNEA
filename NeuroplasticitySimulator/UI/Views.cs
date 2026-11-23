using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Neuroplasticity.Core;
using Neuroplasticity.Data;

namespace Neuroplasticity.UI;

/// <summary>Experiment card (design 1.6.1.3): name, status, progress, and Start/Pause/Stop actions.</summary>
public static class Cards
{
    public static Control Build(MainWindow sh, ExperimentInfo info, List<Action> refreshers)
    {
        var live = sh.Live.FirstOrDefault(s => s.DbId == info.Id);
        string status = live?.Status ?? info.Status;
        int progress = live?.Progress ?? info.Progress;
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = progress, Height = 8, Foreground = UiTheme.B(status == "Completed" ? UiTheme.Good : UiTheme.Accent) };
        var pct = UiTheme.T($"{progress}%", 12, true);
        pct.Margin = new Thickness(8, 0, 0, 0);
        var saved = UiTheme.T(live != null && live.Running ? "Running now" : (status == "Completed" ? "Completed: " : "Last saved: ") + info.Modified, 12, false, UiTheme.Muted);
        if (live != null) refreshers.Add(() => { bar.Value = live.Progress; pct.Text = live.Progress + "%"; });

        var head = new StackPanel
        {
            Spacing = 4,
            Children = { UiTheme.T(info.Name, 16, true), UiTheme.T(info.Architecture + (live != null ? $" · {live.Agents.Count} agent(s)" : ""), 11, false, UiTheme.Muted) }
        };

        var btns = new WrapPanel();
        if (live != null)
        {
            if (!live.Finished) btns.Children.Add(UiTheme.Btn(live.Running ? "Pause" : "Resume", () => sh.ToggleRun(live)));
            btns.Children.Add(UiTheme.Btn("Stop", () => sh.Stop(live), UiTheme.Bad));
            btns.Children.Add(UiTheme.Btn("Open", () => sh.OpenWorkspace(live), outline: true));
        }
        else if (status != "Completed")
        {
            btns.Children.Add(UiTheme.Btn("Resume", () => sh.Resume(info)));
            btns.Children.Add(UiTheme.Btn("Delete", () => sh.Delete(info), outline: true));
        }
        else
        {
            btns.Children.Add(UiTheme.Btn("View Results", () => sh.ShowResults(info.Id), UiTheme.Accent));
            btns.Children.Add(UiTheme.Btn("Delete", () => sh.Delete(info), outline: true));
        }
        var barRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        barRow.Children.Add(bar); Grid.SetColumn(pct, 1); barRow.Children.Add(pct);
        var body = new StackPanel { Spacing = 8, Width = 300, Children = { head, UiTheme.Pill(status, UiTheme.StatusColour(status)), saved, barRow, btns } };
        return UiTheme.Card(body);
    }
}

static class ViewHelpers
{
    public static Border Tile(string label, TextBlock value) => new()
    {
        Background = Brushes.Transparent, BorderBrush = UiTheme.B(UiTheme.Line), BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(12, 10), Margin = new Thickness(4),
        Child = new StackPanel { Spacing = 4, Children = { UiTheme.T(label.ToUpperInvariant(), 10, true, UiTheme.Muted), value } }
    };

    public static TextBlock Title(string t) => UiTheme.T(t, 22, true);
}

public class DashboardView : UserControl, IRefreshable
{
    readonly MainWindow sh;
    readonly List<Action> refreshers = new();

    public DashboardView(MainWindow s) { sh = s; Rebuild(); }

    public void Refresh() { foreach (var r in refreshers) r(); }

    public void Rebuild()
    {
        refreshers.Clear();
        var list = sh.Db.ListExperiments(sh.User.Id);
        string StatusOf(ExperimentInfo i) => sh.Live.FirstOrDefault(x => x.DbId == i.Id)?.Status ?? i.Status;
        double hours = list.Sum(i => sh.Live.FirstOrDefault(x => x.DbId == i.Id)?.Elapsed.TotalSeconds ?? i.RuntimeSec) / 3600.0;

        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        var totalTile = ViewHelpers.Tile("Total Experiments", UiTheme.Mono(list.Count.ToString(), 34, true, UiTheme.Accent));
        tiles.Children.Add(totalTile); Grid.SetRowSpan(totalTile, 2);
        var completedTile = ViewHelpers.Tile("Completed", UiTheme.Mono(list.Count(i => StatusOf(i) == "Completed").ToString(), 22, true));
        Grid.SetColumn(completedTile, 1); tiles.Children.Add(completedTile);
        var runningTile = ViewHelpers.Tile("Running", UiTheme.Mono(list.Count(i => StatusOf(i) == "Running").ToString(), 22, true));
        Grid.SetColumn(runningTile, 2); tiles.Children.Add(runningTile);
        var pausedTile = ViewHelpers.Tile("Paused", UiTheme.Mono(list.Count(i => StatusOf(i) == "Paused").ToString(), 22, true));
        Grid.SetColumn(pausedTile, 1); Grid.SetRow(pausedTile, 1); tiles.Children.Add(pausedTile);
        var runtimeTile = ViewHelpers.Tile("Total Runtime", UiTheme.Mono($"{hours:0.0}h", 22, true));
        Grid.SetColumn(runtimeTile, 2); Grid.SetRow(runtimeTile, 1); tiles.Children.Add(runtimeTile);

        var cards = new WrapPanel();
        foreach (var i in list.Take(3)) cards.Children.Add(Cards.Build(sh, i, refreshers));
        if (list.Count == 0) cards.Children.Add(UiTheme.T("No experiments yet – create your first one with “Create New Experiment”.", 14, false, UiTheme.Muted));
        var viewAll = UiTheme.Btn("View All →", sh.ShowExperiments, outline: true);
        var expHead = new DockPanel { Margin = new Thickness(6, 8, 6, 0) };
        DockPanel.SetDock(viewAll, Dock.Right); expHead.Children.Add(viewAll); expHead.Children.Add(UiTheme.T("My Experiments", 18, true));

        var quick = new StackPanel { Spacing = 8, Width = 260 };
        var create = UiTheme.Btn("Create New Experiment", sh.ShowNew); create.HorizontalAlignment = HorizontalAlignment.Stretch;
        var results = UiTheme.Btn("View Results", () => sh.ShowResults(), UiTheme.Accent); results.HorizontalAlignment = HorizontalAlignment.Stretch;
        var export = UiTheme.Btn("Export latest (CSV)", async () => { if (list.Count > 0) await sh.ExportCsvAsync(list[0]); else sh.Notify("Nothing to export yet."); }, UiTheme.Accent);
        export.HorizontalAlignment = HorizontalAlignment.Stretch;
        quick.Children.Add(create); quick.Children.Add(results); quick.Children.Add(export);

        var series = list.Count > 0 ? sh.SeriesFor(list[0]) : new List<ChartSeries>();
        var chart = new ChartControl { Height = 220, FixedMax = 100, Y = p => p.Accuracy, Source = () => series };
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bottom.Children.Add(UiTheme.Card(quick, "Quick Actions"));
        string graphTitle = list.Count > 0 ? $"Your Progress – {list[0].Name} (accuracy %)" : "Your Progress";
        var prog = GraphTools.Card(sh, chart, graphTitle, "progress-graph", () => new ChartControl { Height = 620, FixedMax = 100, Y = chart.Y, Source = chart.Source });
        Grid.SetColumn(prog, 1); bottom.Children.Add(prog);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 6,
                Children = { ViewHelpers.Title($"Welcome back, {sh.User.Username}!"), UiTheme.T("Start, pause or stop your simulations, or create a new one.", 14, false, UiTheme.Muted), tiles, expHead, cards, bottom }
            }
        };
    }
}

public class ExperimentsView : UserControl, IRefreshable
{
    readonly MainWindow sh;
    readonly List<Action> refreshers = new();

    public ExperimentsView(MainWindow s) { sh = s; Rebuild(); }
    public void Refresh() { foreach (var r in refreshers) r(); }

    public void Rebuild()
    {
        refreshers.Clear();
        var list = sh.Db.ListExperiments(sh.User.Id);
        var cards = new WrapPanel();
        foreach (var i in list) cards.Children.Add(Cards.Build(sh, i, refreshers));
        if (list.Count == 0) cards.Children.Add(UiTheme.T("No experiments yet.", 14, false, UiTheme.Muted));
        var head = new DockPanel();
        var add = UiTheme.Btn("＋ New Experiment", sh.ShowNew); DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add); head.Children.Add(ViewHelpers.Title("My Experiments"));
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(24), Spacing = 10, Children = { head, cards } } };
    }
}

/// <summary>Create an experiment. A new simulation always starts with a single agent.</summary>
public class NewExperimentView : UserControl
{
    readonly MainWindow sh;
    string path;
    static readonly int[] Targets = { 1000, 5000, 20000, 50000 };

    public NewExperimentView(MainWindow s)
    {
        sh = s;
        var name = new TextBox { Text = $"Experiment {DateTime.Now:d MMM HH:mm}", Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var ds = new ComboBox { ItemsSource = new[] { "Handwritten digits (built-in)", "CIFAR-10 batch file (data_batch_1.bin)", "My images (folder with one sub-folder per class)" }, SelectedIndex = 0, Width = 360 };
        var pathText = UiTheme.T("", 12, false, UiTheme.Muted);
        var browse = UiTheme.Btn("Browse…", () => { }, outline: true); browse.IsVisible = false;
        var cycles = new ComboBox { ItemsSource = Targets.Select(t => t.ToString("N0") + " cycles").ToList(), SelectedIndex = 1, Width = 200 };
        var stage = new ComboBox { ItemsSource = Stages.All.Select(x => x.Name).ToList(), SelectedIndex = 1, Width = 200 };
        var rule = new ComboBox { ItemsSource = new[] { "Backpropagation", "Hebbian", "Hybrid" }, SelectedIndex = 0, Width = 200 };
        var stageInfo = UiTheme.T("", 12, false, UiTheme.Muted);
        var msg = UiTheme.T("", 13, false, UiTheme.Bad);
        void ShowStage() { var st = Stages.ByName((string)stage.SelectedItem); stageInfo.Text = $"{st.Name}: starts with {st.Pretrain} practice examples, learning rate {st.LearningRate}, forgetting rate {st.ForgettingRate}."; }
        stage.SelectionChanged += (_, _) => ShowStage(); ShowStage();
        ds.SelectionChanged += (_, _) => { browse.IsVisible = ds.SelectedIndex > 0; path = null; pathText.Text = ""; };
        browse.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (ds.SelectedIndex == 1)
            {
                var f = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select CIFAR-10 data_batch_1.bin", AllowMultiple = false });
                if (f.Count > 0) path = f[0].TryGetLocalPath();
            }
            else
            {
                var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder containing one sub-folder of images per class" });
                if (f.Count > 0) path = f[0].TryGetLocalPath();
            }
            pathText.Text = path ?? "";
        };
        var start = UiTheme.Btn("▶  Start Simulation", () =>
        {
            string kind = ds.SelectedIndex switch { 1 => "cifar", 2 => "folder", _ => "digits" };
            if (kind != "digits" && string.IsNullOrEmpty(path)) { msg.Text = "Choose the dataset location with Browse…"; return; }
            try
            {
                var data = Dataset.Load(kind, path);
                sh.CreateSession(string.IsNullOrWhiteSpace(name.Text) ? "Experiment" : name.Text.Trim(), kind, path, data, Targets[cycles.SelectedIndex],
                    Stages.ByName((string)stage.SelectedItem), (RuleKind)rule.SelectedIndex);
            }
            catch (Exception ex) { msg.Text = "Could not load the dataset: " + ex.Message; }
        });
        start.Padding = new Thickness(24, 12); start.HorizontalAlignment = HorizontalAlignment.Left;

        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                UiTheme.T("Experiment name"), name,
                UiTheme.T("Input images"), ds, new StackPanel { Orientation = Orientation.Horizontal, Children = { browse, pathText } },
                UiTheme.T("Total learning cycles"), cycles,
                UiTheme.T("First agent – development stage"), stage, stageInfo,
                UiTheme.T("First agent – learning rule"), rule,
                UiTheme.T("You can add more agents to compare inside the simulation.", 12, false, UiTheme.Muted), msg, start
            }
        };
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(24), Spacing = 10, Children = { ViewHelpers.Title("New Experiment"), UiTheme.Card(form) } } };
    }
}

public class ResultsView : UserControl, IRefreshable
{
    readonly MainWindow sh;
    readonly ChartControl acc, loss;
    readonly TextBlock summary = UiTheme.T("", 13);
    List<ChartSeries> series = new();
    List<ExperimentInfo> list;
    ExperimentInfo shown;

    public ResultsView(MainWindow s, long? id)
    {
        sh = s; list = sh.Db.ListExperiments(sh.User.Id);
        acc = new ChartControl { Height = 260, FixedMax = 100, Y = p => p.Accuracy, Source = () => series };
        loss = new ChartControl { Height = 260, Y = p => p.Loss, Source = () => series };
        var pick = new ComboBox { ItemsSource = list.Select(i => $"{i.Name}  ·  {i.Status}  ·  {i.Modified}").ToList(), Width = 520 };
        var export = UiTheme.Btn("Export CSV", async () => { if (shown != null) await sh.ExportCsvAsync(shown); }, UiTheme.Accent);
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedIndex < 0) return;
            shown = list[pick.SelectedIndex]; series = sh.SeriesFor(shown);
            summary.Text = string.Join("\n", series.Select(x =>
            {
                var last = x.History.LastOrDefault();
                string ev = x.Events.Count == 0 ? "no events" : string.Join("; ", x.Events.Select(e => $"cycle {e.Cycle}: {e.Label}"));
                return $"Agent {x.Id}: {last?.Accuracy:0}% accuracy, loss {last?.Loss:0.00}, {last?.Alive} synapses alive, {last?.Cycle} cycles. Events: {ev}";
            }));
        };
        var charts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var c1 = GraphTools.Card(sh, acc, "Accuracy (%)", "accuracy-graph", () => new ChartControl { Height = 620, FixedMax = 100, Y = acc.Y, Source = acc.Source });
        var c2 = GraphTools.Card(sh, loss, "Loss", "loss-graph", () => new ChartControl { Height = 620, Y = loss.Y, Source = loss.Source }); Grid.SetColumn(c2, 1);
        charts.Children.Add(c1); charts.Children.Add(c2);
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 10,
                Children = { ViewHelpers.Title("Results"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { pick, export } }, charts, UiTheme.Card(summary, "Summary") }
            }
        };
        int idx = id.HasValue ? list.FindIndex(i => i.Id == id.Value) : (list.Count > 0 ? 0 : -1);
        if (idx >= 0) pick.SelectedIndex = idx; else summary.Text = "No experiments to show yet.";
    }

    public void Refresh() { acc.InvalidateVisual(); loss.InvalidateVisual(); }
}

public class AccountView : UserControl
{
    public AccountView(MainWindow sh)
    {
        var cur = new TextBox { PasswordChar = '•', Watermark = "Current password", Width = 320 };
        var nw = new TextBox { PasswordChar = '•', Watermark = "New password", Width = 320 };
        var cf = new TextBox { PasswordChar = '•', Watermark = "Confirm new password", Width = 320 };
        var msg = UiTheme.T("", 13);
        var change = UiTheme.Btn("Change password", () =>
        {
            if (AuthService.Login(sh.Db, sh.User.Username, cur.Text ?? "") == null) { msg.Foreground = UiTheme.B(UiTheme.Bad); msg.Text = "Current password is incorrect."; return; }
            if (nw.Text != cf.Text) { msg.Foreground = UiTheme.B(UiTheme.Bad); msg.Text = "New passwords do not match."; return; }
            var (_, problems) = PasswordPolicy.Check(nw.Text ?? "");
            if (problems.Count > 0) { msg.Foreground = UiTheme.B(UiTheme.Bad); msg.Text = "Password needs: " + string.Join(", ", problems) + "."; return; }
            sh.Db.UpdatePassword(sh.User.Id, Hasher.Hash(nw.Text));
            msg.Foreground = UiTheme.B(UiTheme.Good); msg.Text = "Password updated."; cur.Text = nw.Text = cf.Text = "";
        });
        var body = new StackPanel { Spacing = 8, Children = { UiTheme.T("Signed in as " + sh.User.Username, 15, true), UiTheme.T("Change password", 13, false, UiTheme.Muted), cur, nw, cf, msg, change } };
        Content = new StackPanel { Margin = new Thickness(24), Spacing = 10, Children = { ViewHelpers.Title("Account Settings"), UiTheme.Card(body) } };
    }
}
