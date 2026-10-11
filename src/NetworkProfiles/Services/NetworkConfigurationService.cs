using System.Management;
using System.IO;
using System.Net;
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

    public IReadOnlySet<string> GetMatchingProfileIds(IEnumerable<NetworkProfile> profiles)
    {
        var matching = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in profiles.GroupBy(p => p.AdapterId, StringComparer.OrdinalIgnoreCase))
        {
            AdapterSnapshot snapshot;
            try { snapshot = ReadSnapshot(group.Key); }
            catch { continue; }

            foreach (var profile in group)
            {
                if (Matches(profile, snapshot))
                    matching.Add(profile.Id);
            }
        }
        return matching;
    }

    public ApplyResult Apply(NetworkProfile profile)
    {
        var errors = ProfileValidationService.Validate(profile);
        if (errors.Count > 0)
            return new(false, string.Join(Environment.NewLine, errors));

        AdapterSnapshot? snapshot = null;
        var stage = "reading the adapter’s current settings";
        try
        {
            snapshot = ReadSnapshot(profile.AdapterId);
            // Keep the previous settings in memory for rollback if applying or verification fails.
            stage = "applying profile settings to Windows";
            ApplyCore(profile);
            stage = "reading back the applied settings";
            var actual = ReadSnapshot(profile.AdapterId);
            stage = "verifying the apply result";
            if (!Matches(profile, actual))
                throw new InvalidOperationException("The settings read back from Windows do not match the profile.");

            return new(true, "Profile applied successfully and Windows settings were verified.");
        }
        catch (Exception ex)
        {
            var detail = $"{stage}: {ex.GetType().Name}: {ex.Message}";
            if (snapshot is not null)
            {
                try
                {
                    stage = "restoring previous settings after an error";
                    Restore(snapshot);
                    return new(false, $"Profile could not be applied. Previous settings were restored from memory. Failure stage: {detail}");
                }
                catch (Exception rollbackEx)
                {
                    return new(false, $"CRITICAL: Profile application and automatic rollback both failed. Error: {detail}. Rollback error: {rollbackEx.GetType().Name}: {rollbackEx.Message}");
                }
            }
            return new(false, $"Network settings were not changed. Failure stage: {detail}");
        }
    }

    public void Restore(AdapterSnapshot snapshot)
    {
        using var adapter = FindAdapter(snapshot.AdapterId);
        if (snapshot.DhcpEnabled)
        {
            CheckResult(Invoke(adapter, "EnableDHCP"), "Could not enable DHCP");
            CheckResult(Invoke(adapter, "SetDNSServerSearchOrder", new object?[] { null }), "Could not set DNS to automatic");
            return;
        }

        if (snapshot.IpAddresses.Length == 0 || snapshot.Subnets.Length == 0)
            throw new InvalidOperationException("Cannot restore settings because the snapshot contains no static IPv4 address.");

        CheckResult(Invoke(adapter, "EnableStatic", new object[] { snapshot.IpAddresses, snapshot.Subnets }), "Could not restore previous static IPv4 settings");
        CheckResult(Invoke(adapter, "SetGateways", snapshot.Gateways.Length == 0
            ? new object?[] { Array.Empty<string>(), Array.Empty<ushort>() }
            : new object?[] { snapshot.Gateways, Enumerable.Repeat((ushort)1, snapshot.Gateways.Length).ToArray() }), "Could not restore the previous gateway");
        CheckResult(Invoke(adapter, "SetDNSServerSearchOrder", snapshot.DnsServers.Length == 0
            ? new object?[] { null }
            : new object?[] { snapshot.DnsServers }), "Could not restore previous DNS settings");
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
            CheckResult(Invoke(adapter, "EnableDHCP"), "Could not enable DHCP");
            CheckResult(Invoke(adapter, "SetDNSServerSearchOrder", new object?[] { null }), "Could not set DNS to automatic");
            return;
        }

        var mask = PrefixToMask(profile.PrefixLength!.Value);
        CheckResult(Invoke(adapter, "EnableStatic", new object[]
        {
            new[] { profile.IPv4Address! },
            new[] { mask }
        }), "Could not set static IPv4");

        if (string.IsNullOrWhiteSpace(profile.Gateway))
            CheckResult(Invoke(adapter, "SetGateways", new object?[] { Array.Empty<string>(), Array.Empty<ushort>() }), "Could not clear the gateway");
        else
            CheckResult(Invoke(adapter, "SetGateways", new object?[]
            {
                new[] { profile.Gateway },
                new ushort[] { 1 }
            }), "Could not set the gateway");

        CheckResult(Invoke(adapter, "SetDNSServerSearchOrder",
            profile.DnsServers.Count == 0 ? new object?[] { null } : new object?[] { profile.DnsServers.ToArray() }),
            "Could not set DNS servers");
    }

    private static ManagementObject FindAdapter(string adapterId)
    {
        if (!Guid.TryParse(adapterId.Trim('{', '}'), out var guid))
            throw new InvalidDataException("Invalid adapter ID.");

        var wanted = guid.ToString("D");
        // NetworkInterface.Id is not guaranteed to equal the configuration's
        // SettingID on every driver. Resolve the adapter's InterfaceIndex first.
        var adapterQuery = $"SELECT * FROM Win32_NetworkAdapter WHERE GUID = '{{{wanted}}}'";
        using var adapterSearcher = new ManagementObjectSearcher("root\\CIMV2", adapterQuery);
        using var adapterResults = adapterSearcher.Get();

        foreach (ManagementObject networkAdapter in adapterResults)
        {
            using (networkAdapter)
            {
                if (!Guid.TryParse(Convert.ToString(networkAdapter["GUID"]), out var foundGuid)
                    || foundGuid != guid)
                    continue;

                if (networkAdapter["InterfaceIndex"] is not null
                    && int.TryParse(Convert.ToString(networkAdapter["InterfaceIndex"]), out var interfaceIndex))
                {
                    var configQuery = $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE InterfaceIndex = {interfaceIndex}";
                    using var configSearcher = new ManagementObjectSearcher("root\\CIMV2", configQuery);
                    using var configResults = configSearcher.Get();
                    foreach (ManagementObject config in configResults)
                    {
                        using (config)
                        {
                            if (!int.TryParse(Convert.ToString(config["InterfaceIndex"]), out var foundIndex)
                                || foundIndex != interfaceIndex)
                                continue;

                            var result = new ManagementObject(config.Path);
                            try
                            {
                                result.Get();
                                return result;
                            }
                            catch
                            {
                                result.Dispose();
                                throw;
                            }
                        }
                    }
                }
            }
        }

        // Fallback for drivers where Win32_NetworkAdapter exposes no matching GUID.
        var configFallbackQuery = $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '{{{wanted}}}'";
        using var fallbackSearcher = new ManagementObjectSearcher("root\\CIMV2", configFallbackQuery);
        using var fallbackResults = fallbackSearcher.Get();
        foreach (ManagementObject config in fallbackResults)
        {
            using (config)
            {
                if (!Guid.TryParse(Convert.ToString(config["SettingID"]), out var foundGuid)
                    || foundGuid != guid)
                    continue;

                var result = new ManagementObject(config.Path);
                try
                {
                    result.Get();
                    return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not find the Windows network configuration for adapter (NetworkInterface.Id={wanted}). " +
            "No matching adapter or IPv4 configuration was found in Windows WMI.");
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
            throw new InvalidOperationException($"{message}. Windows management provider code: {code}.");
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

}
