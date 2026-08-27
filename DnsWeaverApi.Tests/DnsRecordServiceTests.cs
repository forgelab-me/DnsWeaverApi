using DnsWeaverApi.Dns;

namespace DnsWeaverApi.Tests;

// DnsRecordService itself talks to a real Windows DNS server via WMI
// (root\MicrosoftDNS) and can't be constructed in a unit test without one —
// DnsWmiScopeProvider connects on construction. IsSupportedType is the one
// piece of pure, WMI-free logic worth covering here; the rest is exercised
// manually against a real DNS server (see project history / README).
public class DnsRecordServiceTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("AAAA")]
    [InlineData("CNAME")]
    [InlineData("TXT")]
    [InlineData("SRV")]
    public void IsSupportedType_accepts_the_five_known_record_types(string type)
    {
        Assert.True(DnsRecordService.IsSupportedType(type));
    }

    [Theory]
    [InlineData("MX")]
    [InlineData("NS")]
    [InlineData("a")] // case-sensitive on purpose — matches WmiClassByType's exact keys
    [InlineData("")]
    public void IsSupportedType_rejects_anything_else(string type)
    {
        Assert.False(DnsRecordService.IsSupportedType(type));
    }
}
