namespace DnsWeaverApi.Models;

public record RecordRequest(string Hostname, string Type, string Value, int Ttl, SrvData? Srv);
