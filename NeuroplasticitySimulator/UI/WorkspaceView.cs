using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Neuroplasticity.Core;

namespace Neuroplasticity.UI;

/// <summary>Small dialog used to add another agent to a running experiment.</summary>
public class AddAgentDialog : Window
{
    public DevelopmentStage Stage = Stages.All[3];
    public RuleKind Kind = RuleKind.Backprop;
    public int Hidden = 24;

    public AddAgentDialog(int existing)
    {
        Title = "Add agent"; Width = 380; Height = 380; CanResize = false; Background = UiTheme.B(UiTheme.CardC);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stage = new ComboBox { ItemsSource = Stages.All.Select(s => s.Name).ToList(), SelectedIndex = Math.Min(3, existing), Width = 320 };
        var rule = new ComboBox { ItemsSource = new[] { "Backpropagation", "Hebbian", "Hybrid" }, SelectedIndex = 0, Width = 320 };
        var size = new Slider { Minimum = 4, Maximum = 64, Value = 24, Width = 320 };
        var sizeT = UiTheme.T("Hidden neurons: 24");
        size.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) sizeT.Text = $"Hidden neurons: {(int)Math.Round(size.Value)}"; };
        var add = UiTheme.Btn("＋ Add agent", () =>
        {
            Stage = Stages.ByName((string)stage.SelectedItem); Kind = (RuleKind)rule.SelectedIndex; Hidden = (int)Math.Round(size.Value);
            Close(true);
        });
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 8,
            Children = { UiTheme.T("Add another agent to compare", 17, true), UiTheme.T("It trains on the same images, starting from cycle 0.", 12, false, UiTheme.Muted),
                         UiTheme.T("Development stage"), stage, UiTheme.T("Learning rule"), rule, sizeT, size, add }
        };
    }
}

/// <summary>Running simulation window (design 1.6.1.4) with one tab per agent.</summary>
public class WorkspaceView : UserControl, IRefreshable
{
    static readonly int[] Speeds = { 1, 5, 20, 100 };
    readonly MainWindow sh;
    readonly SimulationSession S;
    Agent sel;
    bool syncing;

    readonly TextBlock title = UiTheme.T("", 28, true), sub = UiTheme.T("", 13, false, UiTheme.Muted);
    readonly TextBlock tIter = UiTheme.Mono("", 16, true), tAcc = UiTheme.Mono("", 16, true, UiTheme.Accent), tTime = UiTheme.Mono("", 16, true), tEta = UiTheme.Mono("", 16, true);
    readonly Button pauseBtn;
    readonly WrapPanel tabs = new();
    readonly Dictionary<Agent, TextBlock> tabText = new();
    readonly NetworkView net = new() { Height = 400 };
    readonly Border neuronPanel = new();
    readonly TextBlock npInfo = UiTheme.Mono("", 12);
    readonly ComboBox npDamage = new() { ItemsSource = new[] { "10% damage", "25% damage", "50% damage", "Disable neuron" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ChartControl acc;
    readonly HeatmapControl heat = new() { Height = 150 };
    readonly ComboBox stageBox = new(), ruleBox = new(), speedBox = new();
    readonly TextBlock[] insight = { UiTheme.T("", 13), UiTheme.T("", 13), UiTheme.T("", 13), UiTheme.T("", 13), UiTheme.T("", 13) };
    readonly SampleControl sample = new() { Width = 84, Height = 84 };
    readonly TextBlock taskText = UiTheme.T("", 14, true, UiTheme.Soft);
    readonly ProgressBar taskBar = new() { Minimum = 0, Maximum = 100, Height = 8 };
    readonly RingControl ring = new() { Width = 110, Height = 110 };
    readonly TextBlock health = UiTheme.Mono("", 12);
    readonly Slider lr = new() { Minimum = 0.002, Maximum = 0.1 }, fr = new() { Minimum = 0, Maximum = 0.005 }, hid = new() { Minimum = 4, Maximum = 64 };
    readonly TextBlock lrT = UiTheme.Mono("", 11, false, UiTheme.Muted), frT = UiTheme.Mono("", 11, false, UiTheme.Muted), hidT = UiTheme.Mono("", 11, false, UiTheme.Muted);
    readonly ToggleSwitch plast = new() { IsChecked = true };

    public WorkspaceView(MainWindow shell, SimulationSession session)
    {
        sh = shell; S = session; sel = S.Agents[0];
        acc = new ChartControl { Height = 230, FixedMax = 100, Y = p => p.Accuracy, Source = () => S.Agents.Select(a => new ChartSeries(a.Id, S.ColourOf(a), a.History, a.Events)) };

        // ---- header ----
        var back = UiTheme.Btn("Back to Dashboard", sh.ShowDashboard, outline: true);
        pauseBtn = UiTheme.Btn("Pause", () => { sh.ToggleRun(S); Refresh(); });
        var stop = UiTheme.Btn("Stop", () => sh.Stop(S), UiTheme.Bad);
        ToolTip.SetTip(stop, "Stop and finish this simulation. Results are saved.");
        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Children = { Tile("Iteration", tIter), Tile("Accuracy", tAcc), Tile("Elapsed Time", tTime), Tile("ETA", tEta) } };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        head.Children.Add(new StackPanel { Children = { back, title, sub } });
        var tw = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { tiles } };
        Grid.SetColumn(tw, 1); head.Children.Add(tw);
        var bw = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { pauseBtn, stop } };
        Grid.SetColumn(bw, 2); head.Children.Add(bw);

        // ---- neural network card with the neuron-actions panel ----
        var apply = UiTheme.Btn("Apply Injury", () =>
        {
            if (net.Selected is not { } s) return;
            double f = npDamage.SelectedIndex switch { 0 => 0.10, 1 => 0.25, 2 => 0.50, _ => 1.0 };
            sel.InjureNeuron(s.Layer, s.Index, f); ShowNeuron();
        });
        apply.HorizontalAlignment = HorizontalAlignment.Stretch;
        neuronPanel.Background = UiTheme.B(UiTheme.CardC); neuronPanel.BorderBrush = UiTheme.B(UiTheme.Accent); neuronPanel.BorderThickness = new Thickness(1);
        neuronPanel.CornerRadius = new CornerRadius(UiTheme.Radius); neuronPanel.Padding = new Thickness(12); neuronPanel.Width = 230;
        neuronPanel.HorizontalAlignment = HorizontalAlignment.Right; neuronPanel.VerticalAlignment = VerticalAlignment.Top; neuronPanel.Margin = new Thickness(0, 30, 12, 0); neuronPanel.IsVisible = false;
        neuronPanel.Child = new StackPanel { Spacing = 6, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new LineIcon { Kind = "bolt", Width = 16, Height = 16, Stroke = UiTheme.B(UiTheme.Accent) }, UiTheme.T("Neuron Actions", 14, true) } }, npInfo, UiTheme.T("Apply Injury", 12, false, UiTheme.Muted), npDamage, apply } };
        net.SelectionChanged += ShowNeuron;
        var netHost = new Grid(); netHost.Children.Add(net); netHost.Children.Add(neuronPanel);
        var netCard = UiTheme.Card(netHost, "Neural Network – click a neuron to select it");

        // ---- right column ----
        var heatMode = new ComboBox { ItemsSource = new[] { "Activity", "Weights" }, SelectedIndex = 0, Width = 120 };
        heatMode.SelectionChanged += (_, _) => { heat.Mode = (HeatMode)heatMode.SelectedIndex; heat.InvalidateVisual(); };
        var heatBody = new StackPanel { Spacing = 6, Children = { heatMode, heat } };
        var right = new StackPanel { Children = { GraphCard(acc, "Accuracy Over Time (%)", "accuracy-graph", () => CloneChart()), GraphCard(heatBody, "Neural Activity (hidden layer)", "activity-graph", () => CloneHeatmap()) } };
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*") };
        main.Children.Add(netCard); Grid.SetColumn(right, 1); main.Children.Add(right);

        // ---- bottom cards ----
        stageBox.ItemsSource = Stages.All.Select(x => x.Name).ToList();
        ruleBox.ItemsSource = new[] { "Backpropagation", "Hebbian", "Hybrid" };
        speedBox.ItemsSource = new[] { "Slow (1)", "Normal (5)", "Fast (20)", "Turbo (100)" };
        speedBox.SelectedIndex = Math.Max(0, Array.IndexOf(Speeds, S.Speed));
        stageBox.HorizontalAlignment = ruleBox.HorizontalAlignment = speedBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        stageBox.SelectionChanged += (_, _) => { if (!syncing) { sel.ApplyStage(Stages.ByName((string)stageBox.SelectedItem)); Sync(); } };
        ruleBox.SelectionChanged += (_, _) => { if (!syncing) sel.SetRule((RuleKind)ruleBox.SelectedIndex); };
        speedBox.SelectionChanged += (_, _) => S.Speed = Speeds[Math.Max(0, speedBox.SelectedIndex)];
        lr.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !syncing) { sel.LearningRate = lr.Value; lrT.Text = $"Learning Rate: {lr.Value:0.000}"; } };
        fr.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !syncing) { sel.ForgettingRate = fr.Value; frT.Text = $"Forgetting rate: {fr.Value:0.0000}"; } };
        void ApplySize() { int n = (int)Math.Round(hid.Value); if (!syncing && n != sel.Net.Layers[1].Neurons.Length) { sel.Resize(n); net.Selected = null; neuronPanel.IsVisible = false; } }
        hid.PointerCaptureLost += (_, _) => ApplySize();
        hid.LostFocus += (_, _) => ApplySize();
        hid.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) hidT.Text = $"Hidden neurons: {(int)Math.Round(hid.Value)} (release to apply)"; };
        plast.IsCheckedChanged += (_, _) => { if (!syncing) sel.PlasticityEnabled = plast.IsChecked == true; };
        ToolTip.SetTip(plast, "Off freezes the weights: the agent still takes the test but stops learning and forgetting.");
        ToolTip.SetTip(fr, "How quickly unused synapses fade. Very weak ones are pruned.");
        ToolTip.SetTip(lr, "Plasticity: how far each synapse changes per example.");

        var insights = new StackPanel { Spacing = 5 };
        insights.Children.Add(UiTheme.T("Stage", 12, false, UiTheme.Muted)); insights.Children.Add(stageBox);
        insights.Children.Add(UiTheme.T("Learning rule", 12, false, UiTheme.Muted)); insights.Children.Add(ruleBox);
        foreach (var t in insight) insights.Children.Add(t);

        var task = new StackPanel
        {
            Spacing = 8,
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { sample, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { taskText } } } },
                         UiTheme.T("Experiment progress", 12, false, UiTheme.Muted), taskBar }
        };
        var healthRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { ring, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { health } } } };
        var injureNet = UiTheme.Btn("Injure network (−40%)", () => sel.Injure(0.4), outline: true);
        ToolTip.SetTip(injureNet, "Permanently removes 40% of this agent's remaining synapses at random.");
        var controls = new StackPanel
        {
            Spacing = 4,
            Children = { lrT, lr, frT, fr, hidT, hid, new DockPanel { Children = { plast, UiTheme.T("Enable Plasticity", 13) } }, UiTheme.T("Speed", 12, false, UiTheme.Muted), speedBox, injureNet }
        };
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("1.25*,*,1.1*,1.35*") };
        bottom.Children.Add(UiTheme.Card(insights, "Plasticity Insights"));
        var taskCard = UiTheme.Card(task, "Current Task"); Grid.SetColumn(taskCard, 1); bottom.Children.Add(taskCard);
        var healthCard = UiTheme.Card(healthRow, "Network Health"); Grid.SetColumn(healthCard, 2); bottom.Children.Add(healthCard);
        var controlCard = UiTheme.Card(controls, "Simulation Controls"); Grid.SetColumn(controlCard, 3); bottom.Children.Add(controlCard);

        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(20), Spacing = 6, Children = { head, tabs, main, bottom } } };
        Select(sel);
    }

    static Border Tile(string label, TextBlock value) => new()
    {
        Background = UiTheme.B(UiTheme.CardC), BorderBrush = UiTheme.B(UiTheme.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(UiTheme.Radius),
        Padding = new Thickness(14, 8), Margin = new Thickness(4, 0), MinWidth = 120, Child = new StackPanel { Children = { UiTheme.T(label, 11, false, UiTheme.Muted), value } }
    };

    Border GraphCard(Control graph, string heading, string fileName, Func<Control> makeFullSizeGraph)
        => GraphTools.Card(sh, graph, heading, fileName, makeFullSizeGraph);

    Control CloneChart() => new ChartControl { Height = 620, Source = acc.Source, Y = acc.Y, FixedMax = acc.FixedMax };

    Control CloneHeatmap()
    {
        if (heat.Agent != null) return new HeatmapControl { Height = 620, Agent = heat.Agent, Mode = heat.Mode };
        return new Border();
    }

    // ---------------- agents ----------------
    void Select(Agent a)
    {
        sel = a; net.Agent = a; heat.Agent = a; sample.Agent = a; net.Selected = null; neuronPanel.IsVisible = false;
        RebuildTabs(); Sync(); Refresh();
    }

    void RebuildTabs()
    {
        tabs.Children.Clear(); tabText.Clear();
        foreach (var a in S.Agents.ToList())
        {
            var agent = a; bool on = a == sel; var col = S.ColourOf(a);
            var label = UiTheme.T("", 13, on);
            tabText[a] = label;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = UiTheme.B(col), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(label);
            if (S.Agents.Count > 1)
            {
                var x = new Button { Content = "✕", Padding = new Thickness(4, 0), Background = Brushes.Transparent, Foreground = UiTheme.B(UiTheme.Muted), BorderThickness = new Thickness(0), FontSize = 11 };
                ToolTip.SetTip(x, "Remove this agent");
                x.Click += (_, e) => { S.Agents.Remove(agent); Select(agent == sel ? S.Agents[0] : sel); e.Handled = true; };
                row.Children.Add(x);
            }
            var tab = new Border
            {
                Background = UiTheme.B(on ? Color.FromArgb(45, UiTheme.Accent.R, UiTheme.Accent.G, UiTheme.Accent.B) : UiTheme.CardC), BorderBrush = UiTheme.B(on ? col : UiTheme.Line), BorderThickness = new Thickness(on ? 2 : 1),
                CornerRadius = new CornerRadius(UiTheme.Radius), Padding = new Thickness(12, 6), Margin = new Thickness(6, 4), Child = row
            };
            tab.PointerPressed += (_, _) => { if (agent != sel) Select(agent); };
            tabs.Children.Add(tab);
        }
        var add = UiTheme.Btn("＋ Add agent", AddAgent, outline: true);
        add.Margin = new Thickness(6, 4); add.IsEnabled = S.Agents.Count < SimulationSession.MaxAgents;
        ToolTip.SetTip(add, "Add another agent to compare side by side on the same images");
        tabs.Children.Add(add);
    }

    async void AddAgent()
    {
        var dlg = new AddAgentDialog(S.Agents.Count);
        if (!await dlg.ShowDialog<bool>(sh)) return;
        Select(S.AddAgent(dlg.Stage, dlg.Kind, dlg.Hidden));
    }

    void Sync()
    {
        syncing = true;
        stageBox.SelectedItem = sel.Stage.Name; ruleBox.SelectedIndex = (int)sel.Kind;
        lr.Value = sel.LearningRate; fr.Value = sel.ForgettingRate; hid.Value = sel.Net.Layers[1].Neurons.Length; plast.IsChecked = sel.PlasticityEnabled;
        lrT.Text = $"Learning Rate: {sel.LearningRate:0.000}"; frT.Text = $"Forgetting rate: {sel.ForgettingRate:0.0000}"; hidT.Text = $"Hidden neurons: {sel.Net.Layers[1].Neurons.Length} (release to apply)";
        syncing = false;
    }

    void ShowNeuron()
    {
        if (net.Selected is not { } s) { neuronPanel.IsVisible = false; return; }
        var n = sel.Net.Layers[s.Layer].Neurons[s.Index];
        string name = s.Layer == 1 ? $"H{s.Index}" : $"Output {sel.Data.ClassNames[Math.Min(s.Index, sel.Data.ClassNames.Length - 1)]}";
        npInfo.Text = $"Neuron: {name}\nActivity: {n.A:0.00}\nIncoming weights (mean): {n.MeanAbsWeight():0.00}\nSynapses: {n.AliveCount}/{n.W.Length}{(n.Lesioned ? "\n(disabled)" : "")}";
        neuronPanel.IsVisible = true;
    }

    // ---------------- per-tick refresh ----------------
    public void Refresh()
    {
        if (sel == null) return;
        var last = sel.History[^1];
        title.Text = S.Finished ? "Simulation Complete" : S.Running ? "Running Simulation" : "Simulation Paused";
        sub.Text = $"{S.Name}  ·  {S.Data.Name}  ·  {S.Agents.Count} agent(s)";
        tIter.Text = $"{S.Cycle:N0} / {S.TargetCycles:N0}";
        tAcc.Text = $"{last.Accuracy:0.0}%";
        tTime.Text = UiTheme.Hms(S.Elapsed);
        double secs = S.Elapsed.TotalSeconds;
        tEta.Text = S.Finished ? "00:00:00" : (S.Cycle > 0 && secs > 1 && S.Running ? UiTheme.Hms(TimeSpan.FromSeconds((S.TargetCycles - S.Cycle) / (S.Cycle / secs))) : "--:--:--");
        pauseBtn.Content = S.Running ? "Pause" : "Resume"; pauseBtn.IsEnabled = !S.Finished;
        foreach (var kv in tabText) { var h = kv.Key.History[^1]; kv.Value.Text = $"Agent {kv.Key.Id} · {kv.Key.Stage.Name} · {h.Accuracy:0}%"; }

        bool heb = sel.Kind != RuleKind.Backprop, bp = sel.Kind != RuleKind.Hebbian, decay = sel.ForgettingRate > 0;
        string Mark(bool b, string t, string on, string off) => $"{(b ? "✔" : "✖")}  {t}: {(b ? on : off)}";
        insight[0].Text = Mark(heb, "Hebbian Learning", "Active", "Off");
        insight[1].Text = Mark(bp, "Backpropagation", "Active", "Off");
        insight[2].Text = Mark(decay, "Synaptic Decay", "Enabled", "Disabled");
        insight[3].Text = Mark(decay, "Pruning", "Enabled", "Disabled");
        insight[4].Text = Mark(sel.PlasticityEnabled, "Plasticity", "On", "Frozen");

        var ls = sel.LastSample;
        string cn(int i) => i < sel.Data.ClassNames.Length ? sel.Data.ClassNames[i] : i.ToString();
        taskText.Text = ls == null ? "Waiting…" : $"Classifying: {cn(ls.Y)}\nPredicted: {cn(sel.LastPred)} {(sel.LastPred == ls.Y ? "✔" : "✖")}";
        taskBar.Value = S.Progress;
        int total = sel.Net.TotalSynapses, alive = sel.Net.AliveSynapses;
        ring.Fraction = alive / (double)Math.Max(1, total); ring.Label = $"{ring.Fraction * 100:0}%";
        health.Text = $"Connections\nActive: {alive:N0}\nPruned: {total - alive:N0}";
        if (neuronPanel.IsVisible) ShowNeuron();

        net.InvalidateVisual(); heat.InvalidateVisual(); acc.InvalidateVisual(); sample.InvalidateVisual(); ring.InvalidateVisual();
    }

    public void Rebuild() => Refresh();
}
