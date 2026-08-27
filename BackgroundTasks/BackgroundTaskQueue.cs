using System.Threading.Channels;

namespace DnsWeaverApi.BackgroundTasks;

/// <summary>
/// A queued unit of work plus a human-readable description, so
/// QueuedHostedService can log a meaningful line on success or failure
/// instead of a generic "task completed".
/// </summary>
public record QueuedWorkItem(string Description, Func<CancellationToken, Task> Work);

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

    private readonly Channel<QueuedWorkItem> _channel =
        Channel.CreateBounded<QueuedWorkItem>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(string description, Func<CancellationToken, Task> work) =>
        _channel.Writer.TryWrite(new QueuedWorkItem(description, work));

    public IAsyncEnumerable<QueuedWorkItem> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// Number of work items currently waiting to run. -1 if the underlying
    /// channel implementation doesn't support counting (not expected for the
    /// built-in bounded channel used here, but guarded defensively).
    /// </summary>
    public int ApproximateCount => _channel.Reader.CanCount ? _channel.Reader.Count : -1;
}
