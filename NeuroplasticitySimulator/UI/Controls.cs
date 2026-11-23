using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Neuroplasticity.Core;

namespace Neuroplasticity.UI;

public record ChartSeries(string Id, Color Colour, List<MetricPoint> History, List<SimEvent> Events);

public static class Draw
{
    public static readonly IBrush Panel = UiTheme.B(UiTheme.Plot);
    public static readonly IBrush Text = UiTheme.B(UiTheme.Text);
    public static readonly IBrush Dead = new SolidColorBrush(Color.FromRgb(60, 60, 70));

    public static void Txt(DrawingContext c, string s, double x, double y, IBrush b = null, double size = 11) =>
        c.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("SF Mono"), size, b ?? Text), new Point(x, y));

    static byte L(double a, double b, double t) => (byte)(a + (b - a) * t);

    /// <summary>Weight colour: red = positive, blue = negative, pale = near zero.</summary>
    public static Color WeightColour(double w, double scale = 1.0)
    {
        double m = Math.Min(1, Math.Abs(w) / scale);
        return w >= 0 ? Color.FromRgb(L(235, 220, m), L(225, 60, m), L(235, 90, m)) : Color.FromRgb(L(235, 70, m), L(225, 130, m), L(235, 255, m));
    }

    /// <summary>Activity ramp 0..1: deep blue-green to clear teal.</summary>
    public static Color Activity(double t) { t = Math.Clamp(t, 0, 1); return Color.FromRgb(L(20, 105, t), L(43, 198, t), L(48, 199, t)); }
}

/// <summary>Three restrained outline symbols for menu, visibility and injury actions.</summary>
public sealed class LineIcon : Control
{
    public string Kind { get; set; } = "menu";
    public IBrush Stroke { get; set; } = UiTheme.B(UiTheme.Text);
    static readonly Dictionary<string, string> Paths = new()
    {
        ["menu"] = "M3 6 L21 6 M3 12 L21 12 M3 18 L21 18",
        ["bolt"] = "M14 2 L5 13 L11 13 L9 22 L19 10 L13 10 Z",
        ["eye"] = "M2 12 Q12 2 22 12 Q12 22 2 12 Z M12 9 A3 3 0 1 0 12 15 A3 3 0 1 0 12 9"
    };

    public override void Render(DrawingContext ctx)
    {
        if (!Paths.TryGetValue(Kind, out var path)) return;
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale)))
            ctx.DrawGeometry(null, new Pen(Stroke, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geometry.Parse(path));
    }
}

/// <summary>Shared graph toolbar used by dashboard and results charts.</summary>
public static class GraphTools
{
    public static Border Card(Window owner, Control graph, string title, string fileName, Func<Control> fullSizeFactory)
    {
        var export = UiTheme.Btn("Export PNG", async () => await ExportAsync(owner, graph, fileName), outline: true);
        var fullscreen = UiTheme.Btn("Full screen", () => OpenFullscreen(owner, title, fileName, fullSizeFactory), outline: true);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { export, fullscreen } };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions); head.Children.Add(UiTheme.T(title, 15, true));
        return new Border { Background = UiTheme.B(UiTheme.CardC), BorderBrush = UiTheme.B(UiTheme.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(UiTheme.Radius), Padding = new Thickness(14), Margin = new Thickness(4), Child = new StackPanel { Spacing = 8, Children = { head, graph } } };
    }

    static async void OpenFullscreen(Window owner, string title, string fileName, Func<Control> createGraph)
    {
        var graph = createGraph();
        var export = UiTheme.Btn("Export PNG", async () => await ExportAsync(owner, graph, fileName), outline: true);
        var close = UiTheme.Btn("Close", () => { }, outline: true);
        var window = new Window { Title = title, WindowState = WindowState.FullScreen, Background = UiTheme.B(UiTheme.Bg) };
        close.Click += (_, _) => window.Close();
        var toolbar = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Right); toolbar.Children.Add(close); toolbar.Children.Add(export);
        window.Content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { toolbar, graph } };
        Grid.SetRow(graph, 1);
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        refresh.Tick += (_, _) => graph.InvalidateVisual();
        refresh.Start(); window.Closed += (_, _) => refresh.Stop();
        await window.ShowDialog(owner);
    }

    static async Task ExportAsync(Window owner, Control graph, string fileName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export graph as PNG", SuggestedFileName = fileName + ".png", DefaultExtension = "png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } }
        });
        if (file == null) return;
        int width = Math.Max(1, (int)Math.Ceiling(graph.Bounds.Width)), height = Math.Max(1, (int)Math.Ceiling(graph.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(graph);
        await using var stream = await file.OpenWriteAsync();
        bitmap.Save(stream);
        if (owner is MainWindow main) main.Notify("Graph exported as PNG.");
    }
}

/// <summary>Line chart overlaying every agent, with dashed event markers.</summary>
public class ChartControl : Control
{
    public string Title = "";
    public Func<IEnumerable<ChartSeries>> Source;
    public Func<MetricPoint, double> Y;
    public double? FixedMax;
    public bool ShowTitle = false;

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width, H = Bounds.Height;
        ctx.DrawRectangle(Draw.Panel, null, new Rect(0, 0, W, H));
        double l = 42, r = 10, t = 22, bt = 22, w = W - l - r, h = H - t - bt;
        if (w < 20 || h < 20 || Source == null) return;
        var series = Source().ToList();
        double T = Math.Max(100, series.Select(s => s.History.Count == 0 ? 0 : (double)s.History[^1].Cycle).DefaultIfEmpty(100).Max());
        double ymax = FixedMax ?? Math.Max(1, series.SelectMany(s => s.History).Select(Y).DefaultIfEmpty(1).Max() * 1.1);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), 1);
        for (int i = 0; i <= 4; i++)
        {
            double v = ymax * i / 4, py = t + h - v / ymax * h;
            ctx.DrawLine(grid, new Point(l, py), new Point(l + w, py));
            Draw.Txt(ctx, v.ToString(FixedMax.HasValue ? "0" : "0.0", CultureInfo.InvariantCulture), 6, py - 7);
        }
        Draw.Txt(ctx, "cycles: " + (int)T, l, H - 17);
        double lx = l;
        foreach (var s in series)
        {
            var br = new SolidColorBrush(s.Colour);
            ctx.DrawEllipse(br, null, new Point(lx + 4, 11), 4, 4); Draw.Txt(ctx, "Agent " + s.Id, lx + 11, 4, br, 11); lx += 70;
            var pen = new Pen(br, 2);
            var pts = s.History.Select(p => new Point(l + p.Cycle / T * w, t + h - Math.Min(ymax, Y(p)) / ymax * h)).ToList();
            for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
            var ev = new Pen(br, 1) { DashStyle = DashStyle.Dash };
            foreach (var e in s.Events.ToList())
            {
                double x = l + e.Cycle / T * w;
                ctx.DrawLine(ev, new Point(x, t), new Point(x, t + h));
                Draw.Txt(ctx, e.Label.Split(':')[0], x + 2, t + 2, br, 9);
            }
        }
    }
}

public enum HeatMode { Activity, Weights }

/// <summary>Neural activity grid (one square per hidden neuron) or receptive-field weight maps.</summary>
public class HeatmapControl : Control
{
    public Agent Agent;
    public HeatMode Mode = HeatMode.Activity;

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width, H = Bounds.Height;
        ctx.DrawRectangle(Draw.Panel, null, new Rect(0, 0, W, H));
        if (Agent == null) return;
        var hid = Agent.Net.Layers[1].Neurons;
        if (Mode == HeatMode.Activity)
        {
            int cols = Math.Max(4, (int)Math.Ceiling(Math.Sqrt(hid.Length * W / Math.Max(1, H))));
            int rows = (hid.Length + cols - 1) / cols;
            double cs = Math.Min((W - 8) / cols, (H - 8) / rows);
            double mx = Math.Max(1e-6, hid.Max(n => Math.Abs(n.A)));
            for (int i = 0; i < hid.Length; i++)
            {
                IBrush b = hid[i].Lesioned ? Draw.Dead : new SolidColorBrush(Draw.Activity(Math.Abs(hid[i].A) / mx));
                ctx.DrawRectangle(b, null, new Rect(4 + (i % cols) * cs, 4 + (i / cols) * cs, cs - 2, cs - 2));
            }
            return;
        }
        int side = Agent.Data.Side, c2 = 8, r2 = (hid.Length + c2 - 1) / c2;
        double tile = Math.Min(W / c2, (H - 4) / Math.Max(1, r2)) - 4, cell = tile / side;
        for (int i = 0; i < hid.Length; i++)
        {
            double ox = (i % c2) * (tile + 4) + 4, oy = (i / c2) * (tile + 4) + 2; var n = hid[i];
            for (int j = 0; j < n.W.Length; j++)
            {
                IBrush b = n.Alive[j] ? new SolidColorBrush(Draw.WeightColour(n.W[j], 0.8)) : Draw.Dead;
                ctx.DrawRectangle(b, null, new Rect(ox + (j % side) * cell, oy + (j / side) * cell, Math.Max(1, cell - 0.3), Math.Max(1, cell - 0.3)));
            }
        }
    }
}

/// <summary>Input pixels, hidden neurons and outputs with weighted connections. Click a neuron to select it.</summary>
public class NetworkView : Control
{
    public Agent Agent;
    public (int Layer, int Index)? Selected;
    public event Action SelectionChanged;

    double Xof(int layer) => layer == 1 ? Bounds.Width * 0.50 : Bounds.Width * 0.88;
    Point Pos(int layer, int i, int n) => new(Xof(layer), 30 + (Bounds.Height - 44) * (i + 0.5) / n);

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width, H = Bounds.Height;
        ctx.DrawRectangle(Draw.Panel, null, new Rect(0, 0, W, H));
        if (Agent == null || Agent.Net.Layers.Length < 3) return;
        var hid = Agent.Net.Layers[1].Neurons; var outp = Agent.Net.Layers[2].Neurons;
        Draw.Txt(ctx, $"Input ({Agent.Net.Inputs})", 8, 4); Draw.Txt(ctx, $"Hidden Layer ({hid.Length})", Xof(1) - 40, 4); Draw.Txt(ctx, $"Output ({outp.Length})", Xof(2) - 30, 4);

        // input pixels (current task image)
        int side = Agent.Data.Side; double box = Math.Min(W * 0.2, H - 50), cell = box / side, ix = 12, iy = 30 + (H - 44 - box) / 2;
        var x = Agent.LastSample?.X;
        for (int j = 0; j < side * side; j++)
        {
            byte v = x == null ? (byte)40 : (byte)(40 + 215 * Math.Clamp(x[j], 0, 1));
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(v, v, v)), null, new Rect(ix + (j % side) * cell, iy + (j / side) * cell, Math.Max(1, cell - 0.6), Math.Max(1, cell - 0.6)));
        }
        for (int i = 0; i < hid.Length; i++)
        {
            double a = hid[i].AliveCount / (double)Math.Max(1, hid[i].W.Length);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(8 + 55 * a), 90, 185, 190)), 1), new Point(ix + box, iy + box / 2), Pos(1, i, hid.Length));
        }
        for (int k = 0; k < outp.Length; k++)
            for (int i = 0; i < hid.Length; i++)
            {
                if (i >= outp[k].W.Length || !outp[k].Alive[i]) continue;   // pruned or lesioned: not drawn
                double w = outp[k].W[i];
                if (Math.Abs(w) < 0.05) continue;
                var c = Draw.WeightColour(w, 1.0);
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B)), 0.4 + Math.Min(2.2, Math.Abs(w) * 1.8)), Pos(1, i, hid.Length), Pos(2, k, outp.Length));
            }
        for (int layer = 1; layer <= 2; layer++)
        {
            var ns = layer == 1 ? hid : outp; double rad = Math.Clamp((H - 44) / ns.Length * 0.38, 2.5, 7);
            for (int i = 0; i < ns.Length; i++)
            {
                var p = Pos(layer, i, ns.Length); bool sel = Selected == (layer, i);
                IBrush fill = ns[i].Lesioned ? Brushes.Transparent : new SolidColorBrush(Draw.Activity(0.35 + 0.65 * Math.Min(1, ns[i].MeanAbsWeight() / 0.8)));
                ctx.DrawEllipse(fill, sel ? new Pen(Brushes.White, 2) : new Pen(ns[i].Lesioned ? Brushes.Gray : UiTheme.B(UiTheme.Line), 1), p, sel ? rad + 2 : rad, sel ? rad + 2 : rad);
                if (layer == 2) Draw.Txt(ctx, i < Agent.Data.ClassNames.Length ? Agent.Data.ClassNames[i] : i.ToString(), p.X + 11, p.Y - 7, null, 10);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Agent == null || Agent.Net.Layers.Length < 3) return;
        var pt = e.GetPosition(this); double best = 14; (int, int)? hit = null;
        for (int layer = 1; layer <= 2; layer++)
        {
            int n = Agent.Net.Layers[layer].Neurons.Length;
            for (int i = 0; i < n; i++)
            {
                var p = Pos(layer, i, n); double d = Math.Sqrt((p.X - pt.X) * (p.X - pt.X) + (p.Y - pt.Y) * (p.Y - pt.Y));
                if (d < best) { best = d; hit = (layer, i); }
            }
        }
        Selected = hit; InvalidateVisual(); SelectionChanged?.Invoke();
    }
}

/// <summary>Ring gauge, e.g. the share of synapses still active.</summary>
public class RingControl : Control
{
    public double Fraction; public string Label = "";

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width, H = Bounds.Height, r = Math.Min(W, H) / 2 - 8; var c = new Point(W / 2, H / 2);
        ctx.DrawEllipse(null, new Pen(UiTheme.B(UiTheme.Line), 9), c, r, r);
        double f = Math.Clamp(Fraction, 0, 1);
        if (f >= 0.999) ctx.DrawEllipse(null, new Pen(UiTheme.B(UiTheme.Good), 9), c, r, r);
        else if (f > 0.001)
        {
            double a = -Math.PI / 2, b = a + f * 2 * Math.PI;
            var g = new StreamGeometry();
            using (var cx = g.Open())
            {
                cx.BeginFigure(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), false);
                cx.ArcTo(new Point(c.X + r * Math.Cos(b), c.Y + r * Math.Sin(b)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise);
                cx.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(UiTheme.B(UiTheme.Good), 9, lineCap: PenLineCap.Round), g);
        }
        Draw.Txt(ctx, Label, c.X - 18, c.Y - 10, null, 18);
    }
}

/// <summary>The image the agent is currently classifying.</summary>
public class SampleControl : Control
{
    public Agent Agent;

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width, H = Bounds.Height;
        ctx.DrawRectangle(UiTheme.B(UiTheme.Plot), null, new Rect(0, 0, W, H));
        var x = Agent?.LastSample?.X; if (x == null) return;
        int side = Agent.Data.Side; double cs = Math.Min(W, H) / side;
        for (int j = 0; j < x.Length; j++)
        {
            byte v = (byte)(255 * Math.Clamp(x[j], 0, 1));
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(v, v, v)), null, new Rect((j % side) * cs, (j / side) * cs, cs, cs));
        }
    }
}

