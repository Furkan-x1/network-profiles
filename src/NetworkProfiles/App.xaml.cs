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

        // Do not show the WPF window at all for a tray launch. Showing then
        // hiding it causes a black window flash and may briefly add it to the taskbar.
        // The NotifyIcon is initialized by MainWindow's constructor, and explicit
        // shutdown mode keeps the process alive until the user chooses Exit.
        if (!startMinimized)
            window.Show();
    }
}