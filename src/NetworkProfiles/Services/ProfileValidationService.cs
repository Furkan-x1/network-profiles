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
            errors.Add("Profil adı boş olamaz.");
        else if (profile.Name.Trim().Length > 80)
            errors.Add("Profil adı en fazla 80 karakter olabilir.");

        if (string.IsNullOrWhiteSpace(profile.AdapterId))
            errors.Add("Bir ağ adaptörü seçilmelidir.");

        if (!profile.UseDhcp)
        {
            if (!TryIPv4(profile.IPv4Address, out var address))
                errors.Add("Statik IPv4 adresi geçerli bir IPv4 adresi olmalıdır.");

            if (profile.PrefixLength is null or < 1 or > 32)
                errors.Add("Önek uzunluğu 1 ile 32 arasında olmalıdır.");

            if (!string.IsNullOrWhiteSpace(profile.Gateway) && !TryIPv4(profile.Gateway, out _))
                errors.Add("Ağ geçidi geçerli bir IPv4 adresi olmalıdır.");

            if (TryIPv4(profile.IPv4Address, out address) && address.Equals(IPAddress.Any))
                errors.Add("0.0.0.0 bir cihaz IPv4 adresi olarak kullanılamaz.");
        }

        if (profile.DnsServers is null)
            errors.Add("DNS sunucuları listesi boş olmalıdır veya geçerli IPv4 adresleri içermelidir.");
        else
        {
            if (profile.DnsServers.Count > 4)
                errors.Add("En fazla dört DNS sunucusu tanımlanabilir.");

            foreach (var dns in profile.DnsServers)
                if (!TryIPv4(dns, out _))
                    errors.Add($"Geçersiz DNS IPv4 adresi: {dns}");
        }

        return errors;
    }

    private static bool TryIPv4(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        return !string.IsNullOrWhiteSpace(value)
            && IPAddress.TryParse(value.Trim(), out address!)
            && address.AddressFamily == AddressFamily.InterNetwork;
    }
}
