using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace YaGt.Core.Gui;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) ErrorLog.Fatal(ex); };
        TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write("attività", e.Exception); e.SetObserved(); };
        try
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) { ErrorLog.Fatal(ex); return 1; }
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk)
        {
            desk.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var st = new AppState();
            // prova automatica (GitHub): con YAGT_SKIPSTART=1 e nominativo e locatore già nel file .ini si salta la finestra di avvio
            bool skip = Environment.GetEnvironmentVariable("YAGT_SKIPSTART") == "1" && st.MyCall != "" && Locator.IsValid6(st.MyLoc);
            if (skip) OpenMain(desk, st);
            else
            {
                var w = new StartWindow(st);
                w.Closed += (_, _) =>
                {
                    if (w.Accepted) OpenMain(desk, st);
                    else desk.Shutdown();
                };
                desk.MainWindow = w;
                w.Show();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    static void OpenMain(IClassicDesktopStyleApplicationLifetime desk, AppState st)
    {
        var m = new MainWindow(st);
        m.Closed += (_, _) => { st.Shutdown(); desk.Shutdown(); };
        desk.MainWindow = m;
        m.Show();
    }
}
