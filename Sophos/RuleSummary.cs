namespace DnsWeaverApi.Sophos;

public record RuleSummary(IReadOnlyList<string> Domains, string? BackendIp);
