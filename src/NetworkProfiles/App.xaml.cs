using System.Threading;
using System.Windows;

namespace NetworkProfiles;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, @"Local\NetworkProfiles.Singleton", out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
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
            // Show invisibly so WPF raises Loaded and initializes tray-related UI,
            // without flashing a black window or adding it to the taskbar.
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
