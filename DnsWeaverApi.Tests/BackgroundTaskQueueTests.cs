using DnsWeaverApi.BackgroundTasks;

namespace DnsWeaverApi.Tests;

public class BackgroundTaskQueueTests
{
    [Fact]
    public void TryEnqueue_succeeds_when_below_capacity()
    {
        var queue = new BackgroundTaskQueue();
        var accepted = queue.TryEnqueue("test item", _ => Task.CompletedTask);
        Assert.True(accepted);
    }

    [Fact]
    public void ApproximateCount_reflects_queued_items_not_yet_read()
    {
        var queue = new BackgroundTaskQueue();
        queue.TryEnqueue("item 1", _ => Task.CompletedTask);
        queue.TryEnqueue("item 2", _ => Task.CompletedTask);

        Assert.Equal(2, queue.ApproximateCount);
    }

    [Fact]
    public async Task ReadAllAsync_yields_items_in_FIFO_order()
    {
        var queue = new BackgroundTaskQueue();
        queue.TryEnqueue("first", _ => Task.CompletedTask);
        queue.TryEnqueue("second", _ => Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        var reader = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("first", reader.Current.Description);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("second", reader.Current.Description);
    }

    [Fact]
    public void TryEnqueue_fails_once_capacity_is_exhausted()
    {
        // Nothing drains the queue in this test, so the 501st item (capacity is
        // 500) should be rejected rather than accepted-and-silently-lost.
        var queue = new BackgroundTaskQueue();
        for (var i = 0; i < 500; i++)
        {
            Assert.True(queue.TryEnqueue($"item {i}", _ => Task.CompletedTask));
        }

        var overflowed = queue.TryEnqueue("one too many", _ => Task.CompletedTask);

        Assert.False(overflowed);
    }
}
