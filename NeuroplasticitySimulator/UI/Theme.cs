using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Neuroplasticity.UI;

/// <summary>Light research-workspace tokens with a deep-blue navigation rail and semantic data colours.</summary>
public static class UiTheme
{
    // Visual tokens: deep-blue navigation, pale work surfaces, and distinct scientific data colours.
    public const double Space1 = 4, Space2 = 8, Space3 = 12, Space4 = 16, Space5 = 24;
    public const double Radius = 8;
    public static readonly Color Bg = Color.Parse("#F3F6F8"), Side = Color.Parse("#19334D"), CardC = Color.Parse("#FFFFFF"),
        Line = Color.Parse("#DCE4EA"), Accent = Color.Parse("#315E86"), Soft = Color.Parse("#557B91"), Good = Color.Parse("#34795E"),
        Warn = Color.Parse("#A76D25"), Bad = Color.Parse("#B24E47"), Text = Color.Parse("#203244"), Muted = Color.Parse("#657687"),
        Plot = Color.Parse("#F8FAFC"), SideText = Color.Parse("#EAF1F7"), SideMuted = Color.Parse("#B3C3D0"), SideActive = Color.Parse("#2C4D6A");

    public static IBrush B(Color c) => new SolidColorBrush(c);

    public static TextBlock T(string s, double size = 13, bool bold = false, Color? c = null) => new()
    {
        Text = s, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        FontFamily = new FontFamily("Avenir Next"),
        Foreground = B(c ?? Text), TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Mono(string s, double size = 14, bool bold = false, Color? c = null) => new()
    {
        Text = s, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        FontFamily = new FontFamily("SF Mono"), Foreground = B(c ?? Text), TextWrapping = TextWrapping.NoWrap
    };

    public static Border Card(Control body, string title = null, double pad = 14)
    {
        var sp = new StackPanel { Spacing = 8 };
        if (title != null) sp.Children.Add(T(title, 15, true));
        sp.Children.Add(body);
        return new Border { Background = B(CardC), BorderBrush = B(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius), Padding = new Thickness(pad), Margin = new Thickness(4), Child = sp };
    }

    public static Button Btn(string text, Action click, Color? bg = null, Color? fg = null, bool outline = false)
    {
        var b = new Button
        {
            Content = text, Foreground = B(fg ?? (outline ? Text : Bg)), Background = outline ? B(CardC) : B(bg ?? Accent),
            BorderBrush = B(outline ? Line : Accent), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 8), Margin = new Thickness(0, 0, 6, 0),
            FontFamily = new FontFamily("Avenir Next")
        };
        b.Click += (_, _) => click();
        return b;
    }

    public static Border Pill(string text, Color c) => new()
    {
        Background = B(CardC), BorderBrush = B(c), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Radius),
        Padding = new Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = T(text, 11, true, c)
    };

    public static Color StatusColour(string status) => status switch { "Running" => Accent, "Completed" => Good, "Paused" => Warn, _ => Muted };
    public static string Hms(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
}

public interface IRefreshable
{
    void Refresh();          // called every timer tick
    void Rebuild() { }       // called when experiments/sessions change
}
