using System.Windows;

namespace NetworkProfiles;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
