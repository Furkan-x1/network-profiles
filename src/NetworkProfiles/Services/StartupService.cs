using System.Diagnostics;
using System.IO;

namespace NetworkProfiles.Services;

/// <summary>
/// Registers a logon task that runs the app elevated and minimized.
/// The app requests administrator privileges in its manifest, so a normal
/// Startup-folder shortcut or Run-key entry can be blocked by UAC at logon.
/// </summary>
public sealed class StartupService
{
    private const string TaskName = "\\NetworkProfiles";

    public bool IsEnabled()
    {
        try
        {
            var result = RunSchtasks("/Query", "/TN", TaskName, "/FO", "LIST");
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            var result = RunSchtasks("/Delete", "/TN", TaskName, "/F");
            // Deleting a task that does not exist is already the desired state.
            if (result.ExitCode != 0 && IsEnabled())
                throw new InvalidOperationException($"Could not remove the Windows startup task: {result.Output}");
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Could not locate the application EXE for startup. Run the published executable.");

        // /RL HIGHEST lets Task Scheduler launch this requireAdministrator app
        // at logon without relying on an interactive UAC prompt.
        var taskCommand = $"\"{executable}\" --minimized";
        var create = RunSchtasks(
            "/Create",
            "/TN", TaskName,
            "/TR", taskCommand,
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/IT",
            "/F");

        if (create.ExitCode != 0)
            throw new InvalidOperationException(
                $"Could not register the elevated Windows startup task. {create.Output}".Trim());
    }

    private static (int ExitCode, string Output) RunSchtasks(params string[] arguments)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var executable = Path.Combine(systemDirectory, "schtasks.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Windows Task Scheduler utility was not found.", executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Windows Task Scheduler utility.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, string.Join(Environment.NewLine,
            new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }
}
