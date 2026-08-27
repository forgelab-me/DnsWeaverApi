using System.Net;

namespace DnsWeaverApi.Tests;

/// <summary>
/// Returns canned XML responses in call order, so SophosClient can be tested
/// against realistic Sophos API responses without a real firewall. Each call
/// also captures the decoded reqxml payload so tests can assert on what was
/// actually sent (e.g. that a Set was or wasn't issued).
/// </summary>
internal sealed class SequentialFakeHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses;

    public List<string> CapturedRequestXml { get; } = new();

    public SequentialFakeHandler(params string[] responses)
    {
        _responses = new Queue<string>(responses);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var formBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : "";
        CapturedRequestXml.Add(ExtractReqXml(formBody));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"Test sent more HTTP requests than canned responses were configured for. Requests so far: {CapturedRequestXml.Count}");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responses.Dequeue())
        };
    }

    private static string ExtractReqXml(string formBody)
    {
        const string prefix = "reqxml=";
        var idx = formBody.IndexOf(prefix, StringComparison.Ordinal);
        return idx < 0 ? "" : WebUtility.UrlDecode(formBody[(idx + prefix.Length)..]);
    }
}
