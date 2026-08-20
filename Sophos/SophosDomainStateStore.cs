using System.Collections.Concurrent;

namespace DnsWeaverApi.Sophos;

/// <summary>
/// Sophos WAF rules only store domain presence, not per-domain metadata — there's
/// no "target" to read back. Without this, /list would have to return a constant
/// placeholder value, which never matches dnsweaver's desired state and triggers
/// a spurious /update on every reconciliation cycle for every already-registered
/// domain. This remembers the last value dnsweaver actually sent per (rule, hostname)
/// so /list can echo it back and reconciliation converges to a no-op when nothing
/// really changed.
///
/// In-memory only: resets on app pool recycle. SophosClient.GetRuleSummaryAsync
/// covers most of that gap by resolving the rule's backend IP as a second-best
/// fallback, so a cold cache no longer means a guaranteed spurious /update per
/// domain — not worth persisting this to disk.
/// </summary>
public class SophosDomainStateStore
{
    private readonly ConcurrentDictionary<(string RuleName, string Hostname), string> _lastValues = new();

    public void Remember(string ruleName, string hostname, string value) =>
        _lastValues[(ruleName, hostname)] = value;

    public void Forget(string ruleName, string hostname) =>
        _lastValues.TryRemove((ruleName, hostname), out _);

    public string? TryGetLastValue(string ruleName, string hostname) =>
        _lastValues.TryGetValue((ruleName, hostname), out var value) ? value : null;
}
