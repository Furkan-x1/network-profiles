using System.Net;
using System.Net.Sockets;
using NetworkProfiles.Models;

namespace NetworkProfiles.Services;

/// <summary>Validates profile data before it can be saved or applied.</summary>
public static class ProfileValidationService
{
    public static IReadOnlyList<string> Validate(NetworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
            errors.Add("Profile name cannot be empty.");
        else if (profile.Name.Trim().Length > 80)
            errors.Add("Profile name cannot exceed 80 characters.");

        if (string.IsNullOrWhiteSpace(profile.AdapterId))
            errors.Add("Select a network adapter.");

        if (!profile.UseDhcp)
        {
            if (!TryIPv4(profile.IPv4Address, out var address))
                errors.Add("The static IPv4 address must be a valid IPv4 address.");

            if (profile.PrefixLength is null or < 1 or > 32)
                errors.Add("Prefix length must be between 1 and 32.");

            if (!string.IsNullOrWhiteSpace(profile.Gateway) && !TryIPv4(profile.Gateway, out _))
                errors.Add("Gateway must be a valid IPv4 address.");

            if (TryIPv4(profile.IPv4Address, out address) && address.Equals(IPAddress.Any))
                errors.Add("0.0.0.0 cannot be used as a device IPv4 address.");
        }

        if (profile.DnsServers is null)
            errors.Add("The DNS server list must be empty or contain valid IPv4 addresses.");
        else
        {
            if (profile.DnsServers.Count > 4)
                errors.Add("You can specify up to four DNS servers.");

            foreach (var dns in profile.DnsServers)
                if (!TryIPv4(dns, out _))
                    errors.Add($"Invalid DNS IPv4 address: {dns}");
        }

        return errors;
    }

    private static bool TryIPv4(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(value)
            || !IPAddress.TryParse(value.Trim(), out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;

        address = parsed;
        return true;
    }
}
