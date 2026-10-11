using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetworkProfiles.Services;

public sealed record ScanResult(string Address, long RoundtripTimeMs, string Status);

/// <summary>Opt-in scanner limited to private IPv4 networks and at most 1022 host addresses.</summary>
public sealed class NetworkScannerService
{
    public async Task<IReadOnlyList<ScanResult>> ScanAsync(string cidr, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (network, prefix) = ParseCidr(cidr);
        if (!IsPrivate(network))
            throw new ArgumentException("Tarama yalnızca özel IPv4 ağlarında (10/8, 172.16/12, 192.168/16) yapılabilir.");
        if (prefix < 22)
            throw new ArgumentException("Güvenlik ve performans için ağ aralığı en fazla 1022 cihaz içerebilir (önek /22 veya daha dar).");

        var networkValue = ToUInt32(network);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var first = (networkValue & mask) + 1;
        var last = (networkValue | ~mask) - 1;
        if (last < first)
            throw new ArgumentException("Bu CIDR aralığında taranabilir ana makine yok.");

        var addresses = Enumerable.Range(0, checked((int)(last - first + 1)))
            .Select(i => FromUInt32(first + (uint)i).ToString()).ToArray();
        var results = new System.Collections.Concurrent.ConcurrentBag<ScanResult>();
        var completed = 0;
        using var gate = new SemaphoreSlim(32, 32);

        var tasks = addresses.Select(async address =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var ping = new Ping();
                try
                {
                    var reply = await ping.SendPingAsync(IPAddress.Parse(address), 450).WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success)
                        results.Add(new ScanResult(address, reply.RoundtripTime, "Yanıt verdi"));
                }
                catch (PingException) { }
                catch (TimeoutException) { }
            }
            finally
            {
                gate.Release();
                var count = Interlocked.Increment(ref completed);
                progress?.Report(count);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OrderBy(r => ToUInt32(IPAddress.Parse(r.Address))).ToArray();
    }

    private static (IPAddress Network, int Prefix) ParseCidr(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out var prefix) || prefix is < 16 or > 30)
            throw new ArgumentException("CIDR biçimi geçersiz. Örnek: 192.168.1.0/24");

        var value = ToUInt32(address);
        var mask = uint.MaxValue << (32 - prefix);
        return (FromUInt32(value & mask), prefix);
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
}