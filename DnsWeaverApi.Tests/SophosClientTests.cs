using DnsWeaverApi.Configuration;
using DnsWeaverApi.Sophos;

namespace DnsWeaverApi.Tests;

public class SophosClientTests
{
    private const string LoginOk = "<Login><status>Authentication Successful</status></Login>";

    private static SophosClient CreateClient(SequentialFakeHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://sophos.test/") };
        var options = new SophosOptions
        {
            ApiUrl = "http://sophos.test/",
            Username = "api-user",
            Password = "api-password",
            CertificateThumbprint = "unused-in-tests",
        };
        return new SophosClient(httpClient, options);
    }

    private static string RuleWithDomains(string ruleName, string backend, params string[] domains)
    {
        var domainXml = string.Concat(domains.Select(d => $"<Domain>{d}</Domain>"));
        return $"""
            <Response>{LoginOk}
                <FirewallRule transactionid="">
                    <Name>{ruleName}</Name>
                    <HTTPBasedPolicy>
                        <Domains>{domainXml}</Domains>
                        <AccessPaths><AccessPath><backend>{backend}</backend></AccessPath></AccessPaths>
                    </HTTPBasedPolicy>
                </FirewallRule>
            </Response>
            """;
    }

    private static string RuleNotFound() => $"<Response>{LoginOk}</Response>";

    private static string SetStatus(string entity, string code, string message) =>
        $"""<Response>{LoginOk}<{entity} transactionid=""><Status code="{code}">{message}</Status></{entity}></Response>""";

    private static string GroupWithMembers(string groupName, params string[] members)
    {
        var memberXml = string.Concat(members.Select(m => $"<SecurityPolicy>{m}</SecurityPolicy>"));
        return $"""
            <Response>{LoginOk}
                <FirewallRuleGroup transactionid="">
                    <Name>{groupName}</Name>
                    <SecurityPolicyList>{memberXml}</SecurityPolicyList>
                </FirewallRuleGroup>
            </Response>
            """;
    }

    [Fact]
    public async Task GetFirewallRuleAsync_returns_null_when_the_filter_matches_nothing()
    {
        var handler = new SequentialFakeHandler(RuleNotFound());
        var client = CreateClient(handler);

        var rule = await client.GetFirewallRuleAsync("DOES-NOT-EXIST");

        Assert.Null(rule);
    }

    [Fact]
    public async Task GetFirewallRuleAsync_returns_the_matched_rule()
    {
        var handler = new SequentialFakeHandler(RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "note.forgelab.me"));
        var client = CreateClient(handler);

        var rule = await client.GetFirewallRuleAsync("WAN TO DOCKER-1-FORGELAB");

        Assert.NotNull(rule);
        Assert.Equal("WAN TO DOCKER-1-FORGELAB", rule!.Element("Name")!.Value);
    }

    [Fact]
    public async Task AddDomainAsync_is_a_noop_when_the_domain_is_already_present()
    {
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "app.forgelab.me", "note.forgelab.me"));
        var client = CreateClient(handler);

        var added = await client.AddDomainAsync("WAN TO DOCKER-1-FORGELAB", "app.forgelab.me");

        Assert.False(added);
        // Only the Get should have been sent — no Set for a no-op add.
        Assert.Single(handler.CapturedRequestXml);
    }

    [Fact]
    public async Task AddDomainAsync_sends_a_Set_with_the_new_domain_when_missing()
    {
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "note.forgelab.me"),
            SetStatus("FirewallRule", "200", "Configuration applied successfully."));
        var client = CreateClient(handler);

        var added = await client.AddDomainAsync("WAN TO DOCKER-1-FORGELAB", "app.forgelab.me");

        Assert.True(added);
        Assert.Equal(2, handler.CapturedRequestXml.Count);
        var setRequest = handler.CapturedRequestXml[1];
        Assert.Contains("<Set operation=\"update\">", setRequest);
        Assert.Contains("<Domain>app.forgelab.me</Domain>", setRequest);
        Assert.Contains("<Domain>note.forgelab.me</Domain>", setRequest); // existing domain preserved
    }

    [Fact]
    public async Task AddDomainAsync_throws_when_Sophos_reports_a_non_200_status()
    {
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "note.forgelab.me"),
            SetStatus("FirewallRule", "500", "Configuration failed."));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<SophosOperationException>(
            () => client.AddDomainAsync("WAN TO DOCKER-1-FORGELAB", "app.forgelab.me"));
    }

    [Fact]
    public async Task AddDomainAsync_throws_when_the_rule_does_not_exist()
    {
        var handler = new SequentialFakeHandler(RuleNotFound());
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<SophosOperationException>(
            () => client.AddDomainAsync("DOES-NOT-EXIST", "app.forgelab.me"));
    }

    [Fact]
    public async Task RemoveDomainAsync_returns_false_and_sends_no_Set_when_the_domain_is_absent()
    {
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "note.forgelab.me"));
        var client = CreateClient(handler);

        var removed = await client.RemoveDomainAsync("WAN TO DOCKER-1-FORGELAB", "not-there.forgelab.me");

        Assert.False(removed);
        Assert.Single(handler.CapturedRequestXml);
    }

    [Fact]
    public async Task RemoveDomainAsync_sends_a_Set_without_the_removed_domain()
    {
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "app.forgelab.me", "note.forgelab.me"),
            SetStatus("FirewallRule", "200", "Configuration applied successfully."));
        var client = CreateClient(handler);

        var removed = await client.RemoveDomainAsync("WAN TO DOCKER-1-FORGELAB", "app.forgelab.me");

        Assert.True(removed);
        var setRequest = handler.CapturedRequestXml[1];
        Assert.DoesNotContain("<Domain>app.forgelab.me</Domain>", setRequest);
        Assert.Contains("<Domain>note.forgelab.me</Domain>", setRequest);
    }

    [Fact]
    public async Task GetRuleSummaryAsync_resolves_the_backend_ip_via_an_IPHost_lookup()
    {
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "app.forgelab.me", "note.forgelab.me"),
            $"""<Response>{LoginOk}<IPHost transactionid=""><Name>docker-1</Name><IPAddress>192.168.2.10</IPAddress></IPHost></Response>""");
        var client = CreateClient(handler);

        var summary = await client.GetRuleSummaryAsync("WAN TO DOCKER-1-FORGELAB");

        Assert.Equal(new[] { "app.forgelab.me", "note.forgelab.me" }, summary.Domains);
        Assert.Equal("192.168.2.10", summary.BackendIp);
    }

    [Fact]
    public async Task GetRuleSummaryAsync_returns_empty_when_the_rule_does_not_exist()
    {
        var handler = new SequentialFakeHandler(RuleNotFound());
        var client = CreateClient(handler);

        var summary = await client.GetRuleSummaryAsync("DOES-NOT-EXIST");

        Assert.Empty(summary.Domains);
        Assert.Null(summary.BackendIp);
    }

    [Fact]
    public async Task EnsureRuleInGroupAsync_is_a_noop_when_the_rule_is_already_a_member()
    {
        var handler = new SequentialFakeHandler(
            GroupWithMembers("WAN to LAN", "WAN TO PORTAINER", "WAN TO DOCKER-1-FORGELAB"));
        var client = CreateClient(handler);

        await client.EnsureRuleInGroupAsync("WAN to LAN", "WAN TO DOCKER-1-FORGELAB");

        Assert.Single(handler.CapturedRequestXml); // just the Get, no Set needed
    }

    [Fact]
    public async Task EnsureRuleInGroupAsync_adds_the_rule_when_missing_from_the_group()
    {
        var handler = new SequentialFakeHandler(
            GroupWithMembers("WAN to LAN", "WAN TO PORTAINER"),
            SetStatus("FirewallRuleGroup", "200", "Configuration applied successfully."));
        var client = CreateClient(handler);

        await client.EnsureRuleInGroupAsync("WAN to LAN", "WAN TO DOCKER-1-FORGELAB");

        Assert.Equal(2, handler.CapturedRequestXml.Count);
        var setRequest = handler.CapturedRequestXml[1];
        Assert.Contains("<SecurityPolicy>WAN TO DOCKER-1-FORGELAB</SecurityPolicy>", setRequest);
        Assert.Contains("<SecurityPolicy>WAN TO PORTAINER</SecurityPolicy>", setRequest); // existing member preserved
    }

    [Fact]
    public async Task AddDomainAsync_reasserts_group_membership_after_updating_the_rule()
    {
        // Regression test for the empirically observed Sophos behavior: Set on
        // FirewallRule silently drops the rule from its FirewallRuleGroup.
        var handler = new SequentialFakeHandler(
            RuleWithDomains("WAN TO DOCKER-1-FORGELAB", "docker-1", "note.forgelab.me"),
            SetStatus("FirewallRule", "200", "Configuration applied successfully."),
            GroupWithMembers("WAN to LAN", "WAN TO PORTAINER"), // rule missing from group after the Set
            SetStatus("FirewallRuleGroup", "200", "Configuration applied successfully."));
        var client = CreateClient(handler);

        await client.AddDomainAsync("WAN TO DOCKER-1-FORGELAB", "app.forgelab.me", groupName: "WAN to LAN");

        Assert.Equal(4, handler.CapturedRequestXml.Count);
        Assert.Contains("<SecurityPolicy>WAN TO DOCKER-1-FORGELAB</SecurityPolicy>", handler.CapturedRequestXml[3]);
    }
}
