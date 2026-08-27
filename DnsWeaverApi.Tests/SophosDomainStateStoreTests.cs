using DnsWeaverApi.Sophos;

namespace DnsWeaverApi.Tests;

public class SophosDomainStateStoreTests
{
    [Fact]
    public void Unknown_hostname_returns_null()
    {
        var store = new SophosDomainStateStore();
        Assert.Null(store.TryGetLastValue("RULE", "app.forgelab.me"));
    }

    [Fact]
    public void Remember_then_retrieve_returns_the_stored_value()
    {
        var store = new SophosDomainStateStore();
        store.Remember("RULE", "app.forgelab.me", "192.168.2.10");
        Assert.Equal("192.168.2.10", store.TryGetLastValue("RULE", "app.forgelab.me"));
    }

    [Fact]
    public void Remember_overwrites_the_previous_value()
    {
        var store = new SophosDomainStateStore();
        store.Remember("RULE", "app.forgelab.me", "192.168.2.10");
        store.Remember("RULE", "app.forgelab.me", "192.168.2.11");
        Assert.Equal("192.168.2.11", store.TryGetLastValue("RULE", "app.forgelab.me"));
    }

    [Fact]
    public void Same_hostname_under_different_rules_is_tracked_independently()
    {
        var store = new SophosDomainStateStore();
        store.Remember("RULE-1", "app.forgelab.me", "192.168.2.10");
        store.Remember("RULE-2", "app.forgelab.me", "192.168.2.11");

        Assert.Equal("192.168.2.10", store.TryGetLastValue("RULE-1", "app.forgelab.me"));
        Assert.Equal("192.168.2.11", store.TryGetLastValue("RULE-2", "app.forgelab.me"));
    }

    [Fact]
    public void Forget_removes_the_stored_value()
    {
        var store = new SophosDomainStateStore();
        store.Remember("RULE", "app.forgelab.me", "192.168.2.10");
        store.Forget("RULE", "app.forgelab.me");
        Assert.Null(store.TryGetLastValue("RULE", "app.forgelab.me"));
    }

    [Fact]
    public void Forget_on_unknown_hostname_does_not_throw()
    {
        var store = new SophosDomainStateStore();
        store.Forget("RULE", "never-remembered.forgelab.me");
    }
}
