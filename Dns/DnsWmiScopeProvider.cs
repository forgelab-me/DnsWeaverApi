using System.Management;
using DnsWeaverApi.Configuration;

namespace DnsWeaverApi.Dns;

/// <summary>
/// Owns the WMI connection to the DNS server's root\MicrosoftDNS namespace.
/// A ManagementScope opened once at startup does not recover on its own if the
/// DNS server restarts or a network blip drops the session — without this,
/// every DNS-mode request would fail until the App Pool happens to recycle.
/// </summary>
public class DnsWmiScopeProvider
{
    private readonly object _lock = new();
    private readonly string _path;
    private ManagementScope _scope;

    public DnsWmiScopeProvider(DnsOptions options)
    {
        _path = $@"\\{options.ServerName}\root\MicrosoftDNS";
        _scope = Connect();
    }

    /// <summary>
    /// Returns the current scope, reconnecting first if the last known state
    /// was disconnected. Does not guarantee the connection is actually alive
    /// right now — WMI sessions can go stale without IsConnected noticing —
    /// callers should still handle a ManagementException on use and call
    /// <see cref="Reconnect"/> before retrying.
    /// </summary>
    public ManagementScope GetScope()
    {
        lock (_lock)
        {
            if (!_scope.IsConnected)
            {
                _scope = Connect();
            }
            return _scope;
        }
    }

    /// <summary>
    /// Forces a fresh connection regardless of the current scope's reported
    /// state. Call this after a WMI operation fails, then retry the operation
    /// against the returned scope.
    /// </summary>
    public ManagementScope Reconnect()
    {
        lock (_lock)
        {
            _scope = Connect();
            return _scope;
        }
    }

    private ManagementScope Connect()
    {
        var scope = new ManagementScope(_path);
        scope.Connect();
        return scope;
    }
}
