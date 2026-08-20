using System.Management;
using DnsWeaverApi.Configuration;
using DnsWeaverApi.Models;

namespace DnsWeaverApi.Dns;

/// <summary>
/// Manages DNS resource records on a Windows DNS Server via the legacy WMI
/// provider (root\MicrosoftDNS). Chosen over the DnsServer PowerShell module
/// because that module is Desktop-edition only and can't be hosted cleanly
/// in a PowerShell 7/Core process (see project history for the full story).
/// </summary>
public class DnsRecordService
{
    private static readonly IReadOnlyDictionary<string, string> WmiClassByType = new Dictionary<string, string>
    {
        ["A"] = "MicrosoftDNS_AType",
        ["AAAA"] = "MicrosoftDNS_AAAAType",
        ["CNAME"] = "MicrosoftDNS_CNAMEType",
        ["TXT"] = "MicrosoftDNS_TXTType",
        ["SRV"] = "MicrosoftDNS_SRVType",
    };

    private readonly ManagementScope _scope;
    private readonly DnsOptions _options;

    public DnsRecordService(ManagementScope scope, DnsOptions options)
    {
        _scope = scope;
        _options = options;
    }

    public static bool IsSupportedType(string type) => WmiClassByType.ContainsKey(type);

    private static string WqlEscape(string s) => s.Replace("'", "''");

    public IReadOnlyList<object> List()
    {
        var records = new List<object>();
        foreach (var (type, wmiClass) in WmiClassByType)
        {
            using var searcher = new ManagementObjectSearcher(_scope,
                new ObjectQuery($"SELECT * FROM {wmiClass} WHERE ContainerName='{WqlEscape(_options.Zone)}'"));

            ManagementObjectCollection results;
            try
            {
                results = searcher.Get();
            }
            catch (ManagementException ex)
            {
                throw new WmiOperationException($"WMI error listing {type} records: {ex.Message} ({ex.ErrorCode})");
            }

            foreach (ManagementObject rr in results)
            {
                var owner = (string)rr["OwnerName"];
                var ttl = (uint)rr["TTL"];
                object? entry = type switch
                {
                    "A" => new { hostname = owner, type, value = (string)rr["IPAddress"], ttl },
                    "AAAA" => new { hostname = owner, type, value = (string)rr["IPv6Address"], ttl },
                    "CNAME" => new { hostname = owner, type, value = (string)rr["PrimaryName"], ttl },
                    "TXT" => new { hostname = owner, type, value = (string)rr["DescriptiveText"], ttl },
                    "SRV" => new
                    {
                        hostname = owner,
                        type,
                        value = (string)rr["SRVDomainName"],
                        ttl,
                        srv = new { priority = (ushort)rr["Priority"], weight = (ushort)rr["Weight"], port = (ushort)rr["Port"] }
                    },
                    _ => null
                };
                if (entry is not null) records.Add(entry);
                rr.Dispose();
            }
        }
        return records;
    }

    public void Create(RecordRequest body)
    {
        var wmiClass = WmiClassByType[body.Type];

        using var mc = new ManagementClass(_scope, new ManagementPath(wmiClass), null);
        using var inParams = mc.GetMethodParameters("CreateInstanceFromPropertyData");
        inParams["DnsServerName"] = _options.ServerName;
        inParams["ContainerName"] = _options.Zone;
        inParams["OwnerName"] = body.Hostname;
        inParams["TTL"] = (uint)body.Ttl;
        ApplyValue(inParams, body.Type, body.Value, body.Srv);

        Invoke(mc, inParams);
    }

    public void Update(UpdateRequest body)
    {
        var wmiClass = WmiClassByType[body.Type];

        // Read-modify-write isn't available generically across the 5 WMI record
        // types without per-type Modify() signatures — delete+recreate is simpler
        // and behaviorally equivalent for our purposes.
        DeleteExisting(wmiClass, body.Hostname);

        using var mc = new ManagementClass(_scope, new ManagementPath(wmiClass), null);
        using var inParams = mc.GetMethodParameters("CreateInstanceFromPropertyData");
        inParams["DnsServerName"] = _options.ServerName;
        inParams["ContainerName"] = _options.Zone;
        inParams["OwnerName"] = body.Hostname;
        inParams["TTL"] = (uint)body.Ttl;
        ApplyValue(inParams, body.Type, body.NewValue, body.Srv);

        Invoke(mc, inParams);
    }

    public void Delete(DeleteRequest body)
    {
        var wmiClass = WmiClassByType[body.Type!];
        DeleteExisting(wmiClass, body.Hostname);
    }

    private void DeleteExisting(string wmiClass, string hostname)
    {
        using var searcher = new ManagementObjectSearcher(_scope,
            new ObjectQuery($"SELECT * FROM {wmiClass} WHERE OwnerName='{WqlEscape(hostname)}' AND ContainerName='{WqlEscape(_options.Zone)}'"));
        foreach (ManagementObject rr in searcher.Get())
        {
            rr.Delete();
            rr.Dispose();
        }
    }

    private static void ApplyValue(ManagementBaseObject inParams, string type, string value, SrvData? srv)
    {
        switch (type)
        {
            case "A": inParams["IPAddress"] = value; break;
            case "AAAA": inParams["IPv6Address"] = value; break;
            case "CNAME": inParams["PrimaryName"] = value; break;
            case "TXT": inParams["DescriptiveText"] = value; break;
            case "SRV":
                if (srv is null) throw new ArgumentException("srv data required for SRV records");
                inParams["Priority"] = srv.Priority;
                inParams["Weight"] = srv.Weight;
                inParams["Port"] = srv.Port;
                inParams["SRVDomainName"] = value;
                break;
        }
    }

    private static void Invoke(ManagementClass mc, ManagementBaseObject inParams)
    {
        try
        {
            mc.InvokeMethod("CreateInstanceFromPropertyData", inParams, null);
        }
        catch (ManagementException ex)
        {
            throw new WmiOperationException($"WMI error: {ex.Message} ({ex.ErrorCode})");
        }
    }
}
