namespace NetworkProfiles.Models;

/// <summary>Configuration data for one network adapter; it does not apply itself.</summary>
public sealed record NetworkProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Notes { get; init; } = "";
    public string AdapterId { get; init; } = "";
    public bool UseDhcp { get; init; } = true;
    public string? IPv4Address { get; init; }
    public int? PrefixLength { get; init; }
    public string? Gateway { get; init; }
    public List<string> DnsServers { get; init; } = [];
}