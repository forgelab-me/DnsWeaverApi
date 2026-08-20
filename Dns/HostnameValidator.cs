using System.Text.RegularExpressions;
using DnsWeaverApi.Configuration;

namespace DnsWeaverApi.Dns;

public class HostnameValidator
{
    // Underscore-led labels (_dnsweaver, _acme-challenge, _dmarc, SRV's
    // _service._proto) are normal, valid DNS practice even though they're
    // disallowed by the stricter RFC 1123 "hostname" rules — this validates
    // DNS labels generally, not host-naming specifically, so underscore is
    // allowed. Leading/trailing hyphen per label is still rejected.
    private static readonly Regex Pattern = new(
        @"^(?!-)[A-Za-z0-9_-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9_-]{1,63}(?<!-))*$",
        RegexOptions.Compiled);

    private readonly string _zone;

    public HostnameValidator(DnsOptions options) => _zone = options.Zone;

    public bool IsValid(string hostname) =>
        !string.IsNullOrWhiteSpace(hostname) && hostname.Length <= 253 && Pattern.IsMatch(hostname) &&
        (hostname.Equals(_zone, StringComparison.OrdinalIgnoreCase) ||
         hostname.EndsWith("." + _zone, StringComparison.OrdinalIgnoreCase));
}
