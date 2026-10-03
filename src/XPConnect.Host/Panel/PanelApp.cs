using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

namespace XPConnect.Host.Panel;

/// <summary>The XPConnect window: starts the server, shows the FMS-style panel, stops the server on close.</summary>
public sealed class PanelApp : Application
{
    public static int Run() => BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PanelApp>().UsePlatformDetect().LogToTrace();

    public override void Initialize()
    {
        Styles.Add(new SimpleTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var controller = new PanelController();
            controller.Start();
            desktop.MainWindow = new PanelWindow(controller);
            desktop.Exit += (_, _) => controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
