using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Neuroplasticity.Data;

namespace Neuroplasticity.UI;

/// <summary>Compact sign-in and registration window using the research-workbench theme.</summary>
public class LoginWindow : Window
{
    static readonly IBrush White = UiTheme.B(UiTheme.Text);
    static readonly IBrush AccentBrush = UiTheme.B(UiTheme.Soft);
    static readonly IBrush Link = UiTheme.B(UiTheme.Soft);
    readonly Border body = new();

    public LoginWindow()
    {
        Title = "Neuroplasticity Simulator"; Width = 660; Height = 700; MinWidth = 560; MinHeight = 600; FontFamily = new FontFamily("Avenir Next");
        Background = UiTheme.B(UiTheme.Bg); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        body.Background = UiTheme.B(UiTheme.CardC); body.BorderBrush = UiTheme.B(UiTheme.Line);
        body.BorderThickness = new Thickness(1); body.CornerRadius = new CornerRadius(UiTheme.Radius); body.Padding = new Thickness(8);
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, Width = 440, Margin = new Thickness(0, 28, 0, 28), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "NEUROPLASTICITY SIMULATOR", Foreground = AccentBrush, FontFamily = new FontFamily("Avenir Next"), FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(30, 0, 0, 8) },
                body
            }
        };
        Content = new ScrollViewer { Content = stack };
        ShowLogin();
    }

    // one boxed field: small label on top, placeholder text inside, optional eye toggle
    static (Border box, TextBox tb) Field(string label, string placeholder, bool password = false, bool eye = false)
    {
        var tb = new TextBox
        {
            Watermark = placeholder, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = White,
            FontSize = 16, PasswordChar = password ? '•' : '\0', VerticalAlignment = VerticalAlignment.Center
        };
        var col = new StackPanel { Children = { new TextBlock { Text = label, Foreground = White, FontSize = 15, Margin = new Thickness(14, 8, 0, 0) }, tb } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(col);
        if (eye)
        {
            var b = new Button { Content = new LineIcon { Kind = "eye", Width = 18, Height = 18, Stroke = White }, Background = Brushes.Transparent, Foreground = White, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => tb.PasswordChar = tb.PasswordChar == '\0' ? '•' : '\0';
            Grid.SetColumn(b, 1); grid.Children.Add(b);
        }
        return (new Border { BorderBrush = UiTheme.B(UiTheme.Line), Background = UiTheme.B(UiTheme.Plot), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(UiTheme.Radius), Padding = new Thickness(8, 4), Margin = new Thickness(0, 4), Child = grid }, tb);
    }

    static Button BigButton(string text, double size) => new()
    {
        Content = text, Background = UiTheme.B(UiTheme.Accent), Foreground = UiTheme.B(UiTheme.Bg), FontSize = size, Height = 44, Margin = new Thickness(0, 18, 0, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(UiTheme.Radius), FontFamily = new FontFamily("Avenir Next")
    };

    static StackPanel Footer(string question, string linkText, Action go)
    {
        var l = new Button { Content = linkText, Background = Brushes.Transparent, Foreground = Link, FontSize = 18, BorderThickness = new Thickness(0) };
        l.Click += (_, _) => go();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10,
            Children = { new TextBlock { Text = question, Foreground = White, FontSize = 18, VerticalAlignment = VerticalAlignment.Center }, l }
        };
    }

    void ShowLogin(string info = null, string username = "")
    {
        var (uBox, user) = Field("Username", "Enter your username"); user.Text = username;
        var (pBox, pw) = Field("Password", "Enter your password", password: true);
        var msg = new TextBlock { Text = info ?? "", Foreground = UiTheme.B(info != null ? UiTheme.Good : UiTheme.Bad), FontSize = 14, TextWrapping = TextWrapping.Wrap };
        var btn = BigButton("Log in", 16);
        void Submit()
        {
            if (string.IsNullOrWhiteSpace(user.Text) || string.IsNullOrEmpty(pw.Text)) { msg.Foreground = UiTheme.B(UiTheme.Bad); msg.Text = "Enter your username and password."; return; }
            var u = AuthService.Login(App.Db, user.Text, pw.Text);
            if (u == null) { msg.Foreground = UiTheme.B(UiTheme.Bad); msg.Text = "Incorrect username or password."; return; }
            new MainWindow(u).Show();
            Close();
        }
        btn.Click += (_, _) => Submit();
        pw.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        body.Child = new StackPanel
        {
            Margin = new Thickness(30, 10, 30, 0),
            Children = { new TextBlock { Text = "Login", Foreground = White, FontSize = 18, Margin = new Thickness(0, 0, 0, 8) }, uBox, pBox, msg, btn, Footer("Don't have an account?", "Register", () => ShowSignUp()) }
        };
    }

    void ShowSignUp()
    {
        var (uBox, user) = Field("Username", "Enter your username");
        var (pBox, pw) = Field("Password", "Enter your password", password: true, eye: true);
        var (cBox, confirm) = Field("Confirm", "Confirm your password", password: true, eye: true);
        var hint = new TextBlock { Foreground = UiTheme.B(UiTheme.Muted), FontSize = 13, TextWrapping = TextWrapping.Wrap, Text = "Password: 12+ characters with upper and lower case, a number and a special character." };
        var msg = new TextBlock { Foreground = UiTheme.B(UiTheme.Bad), FontSize = 14, TextWrapping = TextWrapping.Wrap };
        pw.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || string.IsNullOrEmpty(pw.Text)) return;
            var (score, problems) = PasswordPolicy.Check(pw.Text);
            hint.Text = (score switch { <= 1 => "Weak", 2 or 3 => "Fair", 4 => "Good", _ => "Strong" }) + (problems.Count > 0 ? " – still needs: " + string.Join(", ", problems) : " ✔");
        };
        var btn = BigButton("Create account", 16);
        btn.Click += (_, _) =>
        {
            var err = AuthService.Register(App.Db, user.Text ?? "", null, pw.Text ?? "", confirm.Text ?? "");
            if (err != null) { msg.Text = err; return; }
            ShowLogin("Account created – please log in.", user.Text.Trim());
        };
        body.Child = new StackPanel
        {
            Margin = new Thickness(30, 10, 30, 0),
            Children =
            {
                new TextBlock { Text = "Create account", Foreground = White, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                uBox, pBox, cBox, hint, msg, btn, Footer("Already have an account?", "Log in", () => ShowLogin())
            }
        };
    }
}
