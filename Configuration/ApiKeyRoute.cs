namespace DnsWeaverApi.Configuration;

/// <summary>
/// Resolved from the API key presented on a request. "Dns" routes to the WMI-backed
/// DNS zone; "Sophos" routes to a specific WAF rule's domain list, identified by
/// <see cref="SophosRuleName"/>.
/// </summary>
public class ApiKeyRoute
{
    public string Mode { get; set; } = "";
    public string? SophosRuleName { get; set; }

    // Optional: if the rule is meant to stay a member of a Firewall Rule Group,
    // name it here — Sophos silently drops group membership every time the rule
    // is updated via Set, so we re-assert it after every write. Leave unset if
    // the rule isn't grouped.
    public string? SophosGroupName { get; set; }
}
