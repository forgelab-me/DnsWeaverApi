namespace DnsWeaverApi.BackgroundTasks;

public class QueuedHostedService : BackgroundService
{
    private readonly BackgroundTaskQueue _queue;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(BackgroundTaskQueue queue, ILogger<QueuedHostedService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await workItem(stoppingToken);
            }
            catch (Exception ex)
            {
                // No HTTP caller left to report to by the time this runs —
                // this log is the only signal of a failure. Check it if a
                // Sophos domain doesn't show up after the next reconcile cycle.
                _logger.LogError(ex, "Background Sophos operation failed");
            }
        }
    }
}
