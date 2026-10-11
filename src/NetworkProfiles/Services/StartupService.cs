using Microsoft.Win32;

namespace NetworkProfiles.Services;

/// <summary>Optional per-user startup registration; no administrator rights required.</summary>
public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NetworkProfiles";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Kullanıcı başlangıç kayıt defteri anahtarı açılamadı.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Başlangıç için uygulama EXE yolu bulunamadı. Uygulamayı yayımlanmış EXE olarak çalıştırın.");

        key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);
    }
}