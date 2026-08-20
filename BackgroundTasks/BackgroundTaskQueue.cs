using System.Threading.Channels;

namespace DnsWeaverApi.BackgroundTasks;

/// <summary>
/// Lets request handlers hand off slow work (Sophos WAF rule updates routinely
/// take 30-90s to apply, longer than the API manager's client-side timeout) to
/// run after the HTTP response has already been sent. All services referenced
/// by queued work items must be singleton or safe-after-request-scope (true for
/// everything in this project — see ServiceCollectionExtensions), since there's
/// no per-request DI scope available once the work item actually runs.
///
/// Bounded rather than unbounded: if Sophos is down or slow for an extended
/// period, dnsweaver's minute-scale reconcile loop can otherwise pile up queued
/// writes indefinitely and exhaust memory. 500 is far beyond any realistic
/// homelab backlog (would mean hours of continuous failures at this traffic
/// scale) but still bounds worst case. TryEnqueue returns false when full —
/// callers must surface that as an error rather than silently claiming success.
/// </summary>
public class BackgroundTaskQueue
{
    private const int Capacity = 500;

    private readonly Channel<Func<CancellationToken, Task>> _channel =
        Channel.CreateBounded<Func<CancellationToken, Task>>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(Func<CancellationToken, Task> workItem) =>
        _channel.Writer.TryWrite(workItem);

    public IAsyncEnumerable<Func<CancellationToken, Task>> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
