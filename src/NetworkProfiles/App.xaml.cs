using System.Security.Principal;
using System.Windows;
using NetworkProfiles.Services;

namespace NetworkProfiles;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] == "--apply-request")
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                    throw new UnauthorizedAccessException("Ağ ayarlarını değiştirmek için yönetici yetkisi gerekiyor.");

                NetworkConfigurationService.RunElevatedRequest(e.Args[1]);
            }
            catch (Exception ex)
            {
                try
                {
                    var resultPath = Path.ChangeExtension(e.Args[1], ".result.json");
                    File.WriteAllText(resultPath, System.Text.Json.JsonSerializer.Serialize(
                        new ApplyResult(false, ex.Message)));
                }
                catch { }
            }

            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}