using System.Text.Json.Serialization;

namespace DnsWeaverApi.Models;

// dnsweaver's webhook client sends old_value/new_value/old_srv in snake_case,
// unlike every other payload — kept as explicit overrides of the global
// camelCase naming policy.
public record UpdateRequest(
    string Hostname,
    string Type,
    [property: JsonPropertyName("old_value")] string OldValue,
    [property: JsonPropertyName("new_value")] string NewValue,
    int Ttl,
    SrvData? Srv,
    [property: JsonPropertyName("old_srv")] SrvData? OldSrv
);
