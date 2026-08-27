using DnsWeaverApi.Security;

namespace DnsWeaverApi.Tests;

public class SecureCompareTests
{
    [Fact]
    public void Equal_strings_match()
    {
        Assert.True(SecureCompare.Equals("api-key-123", "api-key-123"));
    }

    [Fact]
    public void Different_strings_do_not_match()
    {
        Assert.False(SecureCompare.Equals("api-key-123", "api-key-456"));
    }

    [Fact]
    public void Different_length_strings_do_not_match()
    {
        Assert.False(SecureCompare.Equals("short", "much-longer-value"));
    }

    [Fact]
    public void Empty_strings_match_each_other()
    {
        Assert.True(SecureCompare.Equals("", ""));
    }

    [Fact]
    public void Case_sensitive()
    {
        Assert.False(SecureCompare.Equals("ApiKey", "apikey"));
    }
}
