using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Api.Reporting;
using InventoryAggregation.Tests.Support;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace InventoryAggregation.Tests.Reporting;

public class ReportCacheTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static (ReportCache Cache, FakeTimeProvider Clock) Create(TimeSpan ttl)
    {
        var clock = new FakeTimeProvider(TestData.T0);
        var cache = new ReportCache(Options.Create(new InventoryApiOptions { CacheDuration = ttl }), clock);
        return (cache, clock);
    }

    private static InventoryReport Report(int minutes) =>
        new(TestData.T0.AddMinutes(minutes), [], new ReportStatistics(0, 0, 0, 0, 0, []));

    [Fact]
    public async Task ServesTheCachedReport_WithinTheTtl()
    {
        var (cache, clock) = Create(Ttl);
        using var cacheLifetime = cache;
        var calls = 0;
        Task<InventoryReport> Factory(CancellationToken _) => Task.FromResult(Report(++calls));

        var first = await cache.GetOrCreateAsync(Factory, Ct.Token);
        clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        var second = await cache.GetOrCreateAsync(Factory, Ct.Token);

        Assert.Equal(1, calls);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task RebuildsTheReport_OnceTheTtlHasElapsed()
    {
        var (cache, clock) = Create(Ttl);
        using var cacheLifetime = cache;
        var calls = 0;
        Task<InventoryReport> Factory(CancellationToken _) => Task.FromResult(Report(++calls));

        var first = await cache.GetOrCreateAsync(Factory, Ct.Token);
        clock.Advance(Ttl);   // expiry is exclusive: at exactly the TTL the entry is stale
        var second = await cache.GetOrCreateAsync(Factory, Ct.Token);

        Assert.Equal(2, calls);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task ZeroTtl_DisablesCaching()
    {
        var (cache, _) = Create(TimeSpan.Zero);
        using var cacheLifetime = cache;
        var calls = 0;
        Task<InventoryReport> Factory(CancellationToken _) => Task.FromResult(Report(++calls));

        await cache.GetOrCreateAsync(Factory, Ct.Token);
        await cache.GetOrCreateAsync(Factory, Ct.Token);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConcurrentCallers_ShareASingleRebuild()
    {
        var (cache, _) = Create(Ttl);
        using var cacheLifetime = cache;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new int[1];

        async Task<InventoryReport> Factory(CancellationToken token)
        {
            Interlocked.Increment(ref calls[0]);
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Report(1);
        }

        var leader = cache.GetOrCreateAsync(Factory, Ct.Token);
        await started.Task;
        var followers = Enumerable.Range(0, 5).Select(_ => cache.GetOrCreateAsync(Factory, Ct.Token)).ToArray();
        release.SetResult();
        var results = await Task.WhenAll(followers.Append(leader));

        Assert.Equal(1, calls[0]);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task ACallerWaitingForTheGate_CanBeCancelled()
    {
        var (cache, _) = Create(Ttl);
        using var cacheLifetime = cache;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<InventoryReport> Slow(CancellationToken token)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Report(1);
        }

        var leader = cache.GetOrCreateAsync(Slow, Ct.Token);
        await started.Task;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.GetOrCreateAsync(static _ => Task.FromResult(Report(2)), cts.Token));

        release.SetResult();
        await leader;
    }

    [Fact]
    public async Task Failures_AreNotCached_AndDoNotLeaveTheGateLocked()
    {
        var (cache, _) = Create(Ttl);
        using var cacheLifetime = cache;
        var calls = 0;
        Task<InventoryReport> Factory(CancellationToken _) =>
            ++calls == 1
                ? throw new InventoryDataException(InventoryDataFailure.Unavailable, "down")
                : Task.FromResult(Report(calls));

        await Assert.ThrowsAsync<InventoryDataException>(() => cache.GetOrCreateAsync(Factory, Ct.Token));
        var recovered = await cache.GetOrCreateAsync(Factory, Ct.Token);

        Assert.Equal(2, calls);
        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task Dispose_ReleasesTheGate()
    {
        var (cache, _) = Create(Ttl);

        cache.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => cache.GetOrCreateAsync(static _ => Task.FromResult(Report(1)), Ct.Token));
    }
}
