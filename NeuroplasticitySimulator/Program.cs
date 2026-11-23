using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Neuroplasticity.Data;
using Neuroplasticity.UI;

namespace Neuroplasticity;

public class App : Application
{
    public static Database Db;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            d.ShutdownMode = ShutdownMode.OnLastWindowClose;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuroplasticitySimulator");
            Db = new Database(Path.Combine(dir, "neuro.db"));
            d.MainWindow = new LoginWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
