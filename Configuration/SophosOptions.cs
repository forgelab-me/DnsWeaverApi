namespace DnsWeaverApi.Configuration;

public class SophosOptions
{
    public string ApiUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    // SHA-1 thumbprint of the Sophos admin interface's TLS certificate. Pinned
    // explicitly rather than relying on chain trust, because that cert is
    // typically self-signed/internal and issued for a hostname rather than
    // the raw IP used to reach it — standard validation always fails here.
    public string CertificateThumbprint { get; set; } = "";
}
