using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkProfiles.Models;

namespace NetworkProfiles.Services;

/// <summary>Read-only adapter discovery using .NET networking APIs.</summary>
public sealed class AdapterDiscoveryService
{
    public IReadOnlyList<AdapterStatus> GetAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n =>
            {
                var props = SafeGetProperties(n);
                var ipv4 = props?.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).Distinct().ToArray() ?? [];
                var gateways = props?.GatewayAddresses.Select(g => g.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(a))
                    .Select(a => a.ToString()).Distinct().ToArray() ?? [];

                return new AdapterStatus(
                    n.Id,
                    n.Name,
                    n.NetworkInterfaceType.ToString(),
                    n.OperationalStatus.ToString(),
                    ipv4.Length == 0 ? "—" : string.Join(", ", ipv4),
                    gateways.Length == 0 ? "—" : string.Join(", ", gateways));
            })
            // Keep connected adapters at the top; sort alphabetically within each group.
            .OrderByDescending(a => string.Equals(a.Status, OperationalStatus.Up.ToString(), StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IPInterfaceProperties? SafeGetProperties(NetworkInterface n)
    {
        try { return n.GetIPProperties(); }
        catch (NetworkInformationException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}