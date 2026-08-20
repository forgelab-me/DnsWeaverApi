using System.Xml.Linq;
using DnsWeaverApi.Configuration;

namespace DnsWeaverApi.Sophos;

/// <summary>
/// Talks to the Sophos Firewall XML API (root: FirewallRule entity, PolicyType
/// HTTPBased) to add/remove a domain from an existing WAF rule's domain list.
/// Sophos requires the full rule object on update (no partial/diff updates),
/// so every write is a read-modify-write against the current rule state.
/// </summary>
public class SophosClient
{
    private readonly HttpClient _http;
    private readonly SophosOptions _options;

    public SophosClient(HttpClient http, SophosOptions options)
    {
        _http = http;
        _options = options;
    }

    private XElement BuildLogin() =>
        new XElement("Login",
            new XElement("Username", _options.Username),
            new XElement("Password", _options.Password));

    private async Task<XElement> PostAsync(XElement body)
    {
        var request = new XElement("Request", BuildLogin(), body);
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["reqxml"] = request.ToString(SaveOptions.DisableFormatting)
        });
        var resp = await _http.PostAsync("", content);
        resp.EnsureSuccessStatusCode();
        return XElement.Parse(await resp.Content.ReadAsStringAsync());
    }

    public async Task<XElement?> GetFirewallRuleAsync(string ruleName)
    {
        var getReq = new XElement("Get",
            new XElement("FirewallRule",
                new XElement("Filter",
                    new XElement("key",
                        new XAttribute("name", "Name"),
                        new XAttribute("criteria", "="),
                        ruleName))));
        var response = await PostAsync(getReq);
        return response.Element("FirewallRule");
    }

    public async Task<bool> AddDomainAsync(string ruleName, string domain, string? groupName = null)
    {
        var rule = await GetFirewallRuleAsync(ruleName)
            ?? throw new SophosOperationException($"Règle Sophos '{ruleName}' introuvable");

        var domainsEl = rule.Element("HTTPBasedPolicy")?.Element("Domains")
            ?? throw new SophosOperationException("Élément Domains absent de la règle");

        var alreadyPresent = domainsEl.Elements("Domain")
            .Any(d => string.Equals(d.Value, domain, StringComparison.OrdinalIgnoreCase));

        if (!alreadyPresent)
        {
            domainsEl.Add(new XElement("Domain", domain));
            await SetFirewallRuleAsync(rule, groupName);
        }
        return !alreadyPresent;
    }

    public async Task<bool> RemoveDomainAsync(string ruleName, string domain, string? groupName = null)
    {
        var rule = await GetFirewallRuleAsync(ruleName)
            ?? throw new SophosOperationException($"Règle Sophos '{ruleName}' introuvable");

        var domainsEl = rule.Element("HTTPBasedPolicy")?.Element("Domains")
            ?? throw new SophosOperationException("Élément Domains absent de la règle");

        var toRemove = domainsEl.Elements("Domain")
            .FirstOrDefault(d => string.Equals(d.Value, domain, StringComparison.OrdinalIgnoreCase));

        if (toRemove is null) return false;

        toRemove.Remove();
        await SetFirewallRuleAsync(rule, groupName);
        return true;
    }

    /// <summary>
    /// Domains currently in the rule, plus the backend's resolved IP — used as
    /// a fallback "desired value" for /list when we have no cached last-known
    /// value for a domain (e.g. right after an app pool recycle empties the
    /// in-memory state store). Only meaningful if dnsweaver is configured to
    /// send that same backend IP as the record target, which is a deliberate
    /// operator choice here, not something we can assume in general.
    /// </summary>
    public async Task<RuleSummary> GetRuleSummaryAsync(string ruleName)
    {
        var rule = await GetFirewallRuleAsync(ruleName);
        if (rule is null) return new RuleSummary(Array.Empty<string>(), null);

        var httpPolicy = rule.Element("HTTPBasedPolicy");
        var domains = httpPolicy?.Element("Domains")?
            .Elements("Domain").Select(d => d.Value).ToList() ?? new List<string>();

        var backendName = httpPolicy?.Element("AccessPaths")?.Element("AccessPath")?.Element("backend")?.Value;
        var backendIp = string.IsNullOrEmpty(backendName) ? null : await GetIPHostAddressAsync(backendName);

        return new RuleSummary(domains, backendIp);
    }

    public async Task<string?> GetIPHostAddressAsync(string hostName)
    {
        var getReq = new XElement("Get",
            new XElement("IPHost",
                new XElement("Filter",
                    new XElement("key",
                        new XAttribute("name", "Name"),
                        new XAttribute("criteria", "="),
                        hostName))));
        var response = await PostAsync(getReq);
        return response.Element("IPHost")?.Element("IPAddress")?.Value;
    }

    private async Task SetFirewallRuleAsync(XElement rule, string? groupName)
    {
        var setReq = new XElement("Set", new XAttribute("operation", "update"), rule);
        var response = await PostAsync(setReq);

        var status = response.Element("FirewallRule")?.Element("Status");
        var code = status?.Attribute("code")?.Value;
        if (code != "200")
        {
            throw new SophosOperationException(
                $"Sophos update failed: {status?.Value ?? "unknown error"} (code {code ?? "?"})");
        }

        // Sophos drops a rule from its FirewallRuleGroup whenever it's updated
        // directly via Set on FirewallRule — group membership lives entirely on
        // the group's own SecurityPolicyList, confirmed empirically (the rule
        // disappeared from "WAN to LAN" after a single /create). Re-assert it
        // every time we touch the rule.
        if (!string.IsNullOrEmpty(groupName))
        {
            var ruleName = rule.Element("Name")?.Value;
            if (!string.IsNullOrEmpty(ruleName))
            {
                await EnsureRuleInGroupAsync(groupName, ruleName);
            }
        }
    }

    public async Task EnsureRuleInGroupAsync(string groupName, string ruleName)
    {
        var getReq = new XElement("Get",
            new XElement("FirewallRuleGroup",
                new XElement("Filter",
                    new XElement("key",
                        new XAttribute("name", "Name"),
                        new XAttribute("criteria", "="),
                        groupName))));
        var getResponse = await PostAsync(getReq);
        var group = getResponse.Element("FirewallRuleGroup")
            ?? throw new SophosOperationException($"Groupe Sophos '{groupName}' introuvable");

        var policyList = group.Element("SecurityPolicyList")
            ?? throw new SophosOperationException("Élément SecurityPolicyList absent du groupe");

        var alreadyMember = policyList.Elements("SecurityPolicy")
            .Any(p => string.Equals(p.Value, ruleName, StringComparison.OrdinalIgnoreCase));

        if (alreadyMember) return;

        policyList.Add(new XElement("SecurityPolicy", ruleName));

        var setReq = new XElement("Set", new XAttribute("operation", "update"), group);
        var setResponse = await PostAsync(setReq);

        var status = setResponse.Element("FirewallRuleGroup")?.Element("Status");
        var code = status?.Attribute("code")?.Value;
        if (code != "200")
        {
            throw new SophosOperationException(
                $"Sophos group update failed: {status?.Value ?? "unknown error"} (code {code ?? "?"})");
        }
    }
}
