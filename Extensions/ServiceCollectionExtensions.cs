using System.Security.Cryptography;
using DnsWeaverApi.BackgroundTasks;
using DnsWeaverApi.Configuration;
using DnsWeaverApi.Dns;
using DnsWeaverApi.Sophos;

namespace DnsWeaverApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDnsWeaverServices(this IServiceCollection services, IConfiguration configuration)
    {
        var dnsOptions = configuration.GetSection("Dns").Get<DnsOptions>() ?? new DnsOptions();
        if (string.IsNullOrEmpty(dnsOptions.Zone))
            throw new InvalidOperationException("Dns:Zone missing in configuration");

        var authOptions = configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        var sophosOptions = configuration.GetSection("Sophos").Get<SophosOptions>() ?? new SophosOptions();
        if (string.IsNullOrEmpty(sophosOptions.ApiUrl) || string.IsNullOrEmpty(sophosOptions.Username) || string.IsNullOrEmpty(sophosOptions.Password))
            throw new InvalidOperationException("Sophos:ApiUrl/Username/Password missing in configuration");
        if (string.IsNullOrEmpty(sophosOptions.CertificateThumbprint))
            throw new InvalidOperationException("Sophos:CertificateThumbprint missing in configuration");

        var apiKeys = configuration.GetSection("ApiKeys").Get<Dictionary<string, ApiKeyRoute>>();
        if (apiKeys is null || apiKeys.Count == 0)
            throw new InvalidOperationException(
                "ApiKeys missing in configuration (set via App Pool environment variables or a secrets file — see web.config)");

        services.AddSingleton(dnsOptions);
        services.AddSingleton(authOptions);
        services.AddSingleton(sophosOptions);
        services.AddSingleton<IReadOnlyDictionary<string, ApiKeyRoute>>(apiKeys);

        services.AddSingleton<DnsWmiScopeProvider>();

        services.AddSingleton<HostnameValidator>();
        services.AddSingleton<DnsRecordService>();

        services.AddHttpClient<SophosClient>(client =>
        {
            client.BaseAddress = new Uri(sophosOptions.ApiUrl);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Sophos's admin cert is internal and reached by IP, not hostname —
            // standard chain/name validation will never pass. Pin the exact
            // expected certificate fingerprint instead of disabling validation
            // entirely. SHA-256, not X509Certificate2.Thumbprint (SHA-1 by
            // default) — must match whatever hash the operator read from the
            // browser's certificate viewer.
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null &&
                string.Equals(
                    cert.GetCertHashString(HashAlgorithmName.SHA256),
                    sophosOptions.CertificateThumbprint,
                    StringComparison.OrdinalIgnoreCase)
        });
        services.AddSingleton<SophosDomainStateStore>();

        services.AddSingleton<BackgroundTaskQueue>();
        services.AddHostedService<QueuedHostedService>();

        return services;
    }
}
