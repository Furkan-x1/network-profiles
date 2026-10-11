namespace NetworkProfiles.Models;
/// <summary>Data model only; applying profiles is not implemented in this scaffold.</summary>
public sealed record NetworkProfile
{
    public string Name { get; init; } = "";
    public string AdapterId { get; init; } = "";
    public bool UseDhcp { get; init; } = true;
    public string? IPv4Address { get; init; }
    public int? PrefixLength { get; init; }
    public string? Gateway { get; init; }
    public List<string> DnsServers { get; init; } = [];
}
