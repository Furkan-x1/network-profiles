using System.Management;
using System.IO;
using System.Net;
using System.Text.Json;
using NetworkProfiles.Models;

namespace NetworkProfiles.Services;

public sealed record AdapterSnapshot(
    string AdapterId,
    bool DhcpEnabled,
    string[] IpAddresses,
    string[] Subnets,
    string[] Gateways,
    string[] DnsServers);

public sealed record ApplyResult(bool Success, string Message, string? BackupPath = null);

/// <summary>Applies one profile to one adapter through Windows' network configuration provider.</summary>
public sealed class NetworkConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AdapterSnapshot ReadSnapshot(string adapterId)
    {
        using var adapter = FindAdapter(adapterId);
        return new AdapterSnapshot(
            adapterId,
            GetBool(adapter, "DHCPEnabled"),
            GetStrings(adapter, "IPAddress"),
            GetStrings(adapter, "IPSubnet"),
            GetStrings(adapter, "DefaultIPGateway"),
            GetStrings(adapter, "DNSServerSearchOrder"));
    }

    public ApplyResult Apply(NetworkProfile profile)
    {
        var errors = ProfileValidationService.Validate(profile);
        if (errors.Count > 0)
            return new(false, string.Join(Environment.NewLine, errors));

        AdapterSnapshot? snapshot = null;
        string? backupPath = null;
        try
        {
            snapshot = ReadSnapshot(profile.AdapterId);
            backupPath = SaveBackup(snapshot, profile);
            ApplyCore(profile);
            var actual = ReadSnapshot(profile.AdapterId);
            if (!Matches(profile, actual))
                throw new InvalidOperationException("Uygulama sonrası doğrulama başarısız oldu; önceki ayarlar geri yükleniyor.");

            return new(true, "Profil başarıyla uygulandı ve Windows ayarları doğrulandı.", backupPath);
        }
        catch (Exception ex)
        {
            if (snapshot is not null)
            {
                try
                {
                    Restore(snapshot);
                    return new(false, $"Profil uygulanamadı. Önceki ayarlar geri yüklendi. Ayrıntı: {ex.Message}", backupPath);
                }
                catch (Exception rollbackEx)
                {
                    return new(false, $"KRİTİK: Profil uygulanamadı ve otomatik geri yükleme de başarısız oldu. Önceki ayar yedeği: {backupPath ?? "oluşturulamadı"}. Uygulama hatası: {ex.Message} Geri yükleme hatası: {rollbackEx.Message}", backupPath);
                }
            }
            return new(false, $"Ağ ayarları değiştirilmedi veya yedek alınamadı: {ex.Message}", backupPath);
        }
    }

    public void Restore(AdapterSnapshot snapshot)
    {
        using var adapter = FindAdapter(snapshot.AdapterId);
        if (snapshot.DhcpEnabled)
        {
            CheckResult(Invoke(adapter, "EnableDHCP"), "DHCP etkinleştirilemedi");
            CheckResult(Invoke(adapter, "SetDNSServerSearchOrder", new object?[] { null }), "DNS otomatiğe alınamadı");
            return;
        }

        if (snapshot.IpAddresses.Length == 0 || snapshot.Subnets.Length == 0)
            throw new InvalidOperationException("Yedekte statik IPv4 adresi bulunamadığından geri yükleme yapılamıyor.");

        CheckResult(Invoke(adapter, "EnableStatic", new object[] { snapshot.IpAddresses, snapshot.Subnets }), "Önceki statik IPv4 ayarları geri yüklenemedi");
        CheckResult(Invoke(adapter, "SetGateways", snapshot.Gateways.Length == 0
            ? new object?[] { Array.Empty<string>(), Array.Empty<ushort>() }
            : new object?[] { snapshot.Gateways, Enumerable.Repeat((ushort)1, snapshot.Gateways.Length).ToArray() }), "Önceki ağ geçidi geri yüklenemedi");
        CheckResult(Invoke(adapter, "SetDNSServerSearchOrder", snapshot.DnsServers.Length == 0
            ? new object?[] { null }
            : new object?[] { snapshot.DnsServers }), "Önceki DNS ayarları geri yüklenemedi");
    }

    private static bool Matches(NetworkProfile profile, AdapterSnapshot snapshot)
    {
        if (profile.UseDhcp)
            return snapshot.DhcpEnabled;
        if (snapshot.DhcpEnabled || !snapshot.IpAddresses.Contains(profile.IPv4Address, StringComparer.OrdinalIgnoreCase))
            return false;
        if (!snapshot.Subnets.Contains(PrefixToMask(profile.PrefixLength!.Value), StringComparer.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(profile.Gateway)
            && !snapshot.Gateways.Contains(profile.Gateway, StringComparer.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(profile.Gateway) && snapshot.Gateways.Length > 0)
            return false;
        return profile.DnsServers.Count == 0 ||
               profile.DnsServers.All(d => snapshot.DnsServers.Contains(d, StringComparer.OrdinalIgnoreCase));
    }

    private static void ApplyCore(NetworkProfile profile)
    {
        using var adapter = FindAdapter(profile.AdapterId);
        if (profile.UseDhcp)
        {
            CheckResult(Invoke(adapter, "EnableDHCP"), "DHCP etkinleştirilemedi");
            CheckResult(Invoke(adapter, "SetDNSServerSearchOrder", new object?[] { null }), "DNS otomatiğe alınamadı");
            return;
        }

        var mask = PrefixToMask(profile.PrefixLength!.Value);
        CheckResult(Invoke(adapter, "EnableStatic", new object[]
        {
            new[] { profile.IPv4Address! },
            new[] { mask }
        }), "Statik IPv4 ayarlanamadı");

        if (string.IsNullOrWhiteSpace(profile.Gateway))
            CheckResult(Invoke(adapter, "SetGateways", new object?[] { Array.Empty<string>(), Array.Empty<ushort>() }), "Ağ geçidi temizlenemedi");
        else
            CheckResult(Invoke(adapter, "SetGateways", new object?[]
            {
                new[] { profile.Gateway },
                new ushort[] { 1 }
            }), "Ağ geçidi ayarlanamadı");

        CheckResult(Invoke(adapter, "SetDNSServerSearchOrder",
            profile.DnsServers.Count == 0 ? new object?[] { null } : new object?[] { profile.DnsServers.ToArray() }),
            "DNS sunucuları ayarlanamadı");
    }

    private static ManagementObject FindAdapter(string adapterId)
    {
        if (!Guid.TryParse(adapterId.Trim('{', '}'), out var guid))
            throw new InvalidDataException("Adaptör kimliği geçersiz.");

        var wanted = guid.ToString("D");
        using var searcher = new ManagementObjectSearcher(
            $"SELECT SettingID FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '{{{wanted}}}'");
        using var matches = searcher.Get();

        foreach (ManagementObject match in matches)
        {
            // Do not return an instance owned by the query collection. Create and bind
            // an independent ManagementObject so disposing the collection cannot invalidate it.
            var objectPath = match.Path.Path;
            var adapter = new ManagementObject(objectPath);
            try
            {
                adapter.Get();
                return adapter;
            }
            catch
            {
                adapter.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException("Bu adaptör etkin IPv4 yapılandırmasına sahip değil veya Windows tarafından bulunamadı.");
    }

    private static object? Invoke(ManagementObject adapter, string method, object?[]? args = null)
    {
        using var input = adapter.GetMethodParameters(method);
        if (input is not null && args is not null)
        {
            switch (method)
            {
                case "EnableStatic":
                    input["IPAddress"] = args[0];
                    input["SubnetMask"] = args[1];
                    break;
                case "SetGateways":
                    input["DefaultIPGateway"] = args[0];
                    input["GatewayCostMetric"] = args[1];
                    break;
                case "SetDNSServerSearchOrder":
                    input["DNSServerSearchOrder"] = args[0];
                    break;
            }
        }

        using var result = adapter.InvokeMethod(method, input, null);
        return result?["ReturnValue"];
    }

    private static void CheckResult(object? value, string message)
    {
        var code = value is null ? 0 : Convert.ToUInt32(value);
        if (code != 0)
            throw new InvalidOperationException($"{message}. Windows yönetim sağlayıcısı kodu: {code}.");
    }

    private static bool GetBool(ManagementObject adapter, string property) =>
        adapter[property] is bool value && value;

    private static string[] GetStrings(ManagementObject adapter, string property) =>
        adapter[property] is string[] values ? values : [];

    private static string PrefixToMask(int prefix)
    {
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return string.Join(".", new[]
        {
            (mask >> 24) & 255, (mask >> 16) & 255, (mask >> 8) & 255, mask & 255
        });
    }

    private static string SaveBackup(AdapterSnapshot snapshot, NetworkProfile profile)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkProfiles", "backups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Profile = profile.Name, Snapshot = snapshot }, JsonOptions));
        return path;
    }
}
