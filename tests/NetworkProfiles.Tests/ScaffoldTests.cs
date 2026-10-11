using NetworkProfiles.Models;
using NetworkProfiles.Services;
using Xunit;

namespace NetworkProfiles.Tests;

public sealed class ProfileValidationTests
{
    [Fact]
    public void ValidStaticProfile_PassesValidation()
    {
        var profile = new NetworkProfile
        {
            Name = "PLC bench",
            AdapterId = "adapter-1",
            UseDhcp = false,
            IPv4Address = "192.168.10.20",
            PrefixLength = 24,
            Gateway = "192.168.10.1",
            DnsServers = ["192.168.10.1", "1.1.1.1"]
        };

        Assert.Empty(ProfileValidationService.Validate(profile));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("2001:db8::1")]
    [InlineData("0.0.0.0")]
    public void InvalidStaticAddress_IsRejected(string address)
    {
        var profile = new NetworkProfile
        {
            Name = "PLC",
            AdapterId = "adapter-1",
            UseDhcp = false,
            IPv4Address = address,
            PrefixLength = 24
        };

        Assert.NotEmpty(ProfileValidationService.Validate(profile));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void InvalidPrefix_IsRejected(int prefix)
    {
        var profile = new NetworkProfile
        {
            Name = "PLC",
            AdapterId = "adapter-1",
            UseDhcp = false,
            IPv4Address = "192.168.10.20",
            PrefixLength = prefix
        };

        Assert.Contains(ProfileValidationService.Validate(profile), error => error.Contains("Önek"));
    }

    [Fact]
    public void InvalidDns_IsRejected()
    {
        var profile = new NetworkProfile
        {
            Name = "PLC",
            AdapterId = "adapter-1",
            UseDhcp = false,
            IPv4Address = "192.168.10.20",
            PrefixLength = 24,
            DnsServers = ["bad-dns"]
        };

        Assert.Contains(ProfileValidationService.Validate(profile), error => error.Contains("DNS"));
    }
}

public sealed class ProfileStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsProfile()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"network-profiles-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "profiles.json");
        try
        {
            var store = new ProfileStore(path);
            var profile = new NetworkProfile
            {
                Name = "PLC line 1",
                Notes = "Test setup",
                AdapterId = "adapter-1",
                UseDhcp = false,
                IPv4Address = "10.20.30.40",
                PrefixLength = 24,
                Gateway = "10.20.30.1",
                DnsServers = ["10.20.30.1"]
            };

            store.Save([profile]);
            var loaded = store.Load();

            var actual = Assert.Single(loaded);
            Assert.Equal(profile.Id, actual.Id);
            Assert.Equal(profile.Name, actual.Name);
            Assert.Equal(profile.IPv4Address, actual.IPv4Address);
            Assert.Equal(profile.DnsServers, actual.DnsServers);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}