using DnsWeaverApi.Configuration;
using DnsWeaverApi.Dns;

namespace DnsWeaverApi.Tests;

public class HostnameValidatorTests
{
    private readonly HostnameValidator _validator = new(new DnsOptions { Zone = "forgelab.me" });

    [Theory]
    [InlineData("forgelab.me")] // apex
    [InlineData("app.forgelab.me")]
    [InlineData("deeply.nested.app.forgelab.me")]
    [InlineData("_dnsweaver.app.forgelab.me")] // dnsweaver ownership TXT records
    [InlineData("_acme-challenge.forgelab.me")]
    [InlineData("_minecraft._tcp.forgelab.me")] // SRV-style
    [InlineData("APP.FORGELAB.ME")] // case-insensitive zone match
    [InlineData("a-b-c.forgelab.me")] // internal hyphens fine
    public void Accepts_valid_in_zone_hostnames(string hostname)
    {
        Assert.True(_validator.IsValid(hostname));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("app.evil.com")] // wrong zone entirely
    [InlineData("evilforgelab.me")] // looks similar but isn't a subdomain of the zone
    [InlineData("forgelab.me.evil.com")] // zone as a prefix, not a suffix
    [InlineData("-app.forgelab.me")] // leading hyphen in a label
    [InlineData("app-.forgelab.me")] // trailing hyphen in a label
    [InlineData("app .forgelab.me")] // embedded space
    [InlineData("app'.forgelab.me")] // quote — would break naive WQL interpolation if it slipped through
    public void Rejects_invalid_or_out_of_zone_hostnames(string? hostname)
    {
        Assert.False(_validator.IsValid(hostname!));
    }

    [Fact]
    public void Rejects_hostname_exceeding_253_characters()
    {
        var label = new string('a', 63);
        var tooLong = string.Join('.', Enumerable.Repeat(label, 5)) + ".forgelab.me";
        Assert.True(tooLong.Length > 253);
        Assert.False(_validator.IsValid(tooLong));
    }

    [Fact]
    public void Rejects_label_exceeding_63_characters()
    {
        var label = new string('a', 64);
        Assert.False(_validator.IsValid($"{label}.forgelab.me"));
    }
}
