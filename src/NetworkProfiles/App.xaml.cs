using System.Threading;
using System.Windows;

namespace NetworkProfiles;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, @"Local\\NetworkProfiles.Singleton", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var window = new MainWindow(startMinimized);
        MainWindow = window;

        if (startMinimized)
        {
            // Create the WPF handle and fire Loaded so tray-only startup works,
            // but keep the window invisible and off the taskbar during startup.
            window.ShowInTaskbar = false;
            window.WindowState = WindowState.Minimized;
            window.Opacity = 0;
            window.Show();
        }
        else
        {
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
