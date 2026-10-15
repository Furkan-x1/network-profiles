using System.Windows;

namespace NetworkProfiles;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var window = new MainWindow(startMinimized);
        MainWindow = window;

        if (startMinimized)
        {
            // Keep the startup instance out of the taskbar and hide it before
            // the first normal interaction; the tray icon remains available.
            window.ShowInTaskbar = false;
            window.WindowState = WindowState.Minimized;
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }
    }
}