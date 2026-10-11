using System.Diagnostics;
using System.IO;
namespace NetworkProfiles.Services;
/// <summary>Guarded fallback process wrapper; unused by the current read-only UI.</summary>
internal sealed class SecureNetshRunner
{
    private static readonly string NetshPath = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "netsh.exe"));
    internal async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(NetshPath)) throw new FileNotFoundException("Windows netsh.exe was not found.", NetshPath);
        var startInfo = new ProcessStartInfo {
            FileName = NetshPath, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Could not start netsh.exe.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}
