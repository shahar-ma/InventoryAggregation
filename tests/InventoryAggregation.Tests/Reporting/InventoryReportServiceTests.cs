using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Api.Reporting;
using InventoryAggregation.Tests.Support;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace InventoryAggregation.Tests.Reporting;

public class InventoryReportServiceTests
{
    private sealed class FakeSource(Func<RetrievedInventory> factory) : IInventoryRecordSource
    {
        public int Calls;

        public Task<RetrievedInventory> GetAllAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(factory());
        }
    }

    private static (InventoryReportService Service, ReportCache Cache, FakeTimeProvider Clock) Create(FakeSource source)
    {
        var clock = new FakeTimeProvider(TestData.T0);
        var cache = new ReportCache(Options.Create(new InventoryApiOptions { CacheDuration = TimeSpan.FromMinutes(5) }), clock);
        return (new InventoryReportService(source, new InventoryAggregator(), cache, clock), cache, clock);
    }

    private static RetrievedInventory Mixed() => new(
        [
            TestData.Raw(1, "Electronics", 10m, 2),
            TestData.Raw(2, "Electronics", 99m, 0),            // zero quantity
            TestData.Raw(3, "Furniture", -5m, 1),               // invalid
            null,                                                // invalid
            TestData.Raw(5, "Furniture", 100m, 1),
        ],
        DuplicatesDiscarded: 1);

    [Fact]
    public async Task GetReportAsync_RetrievesSanitizesAndAggregates_WithAccurateStatistics()
    {
        var (service, cache, _) = Create(new FakeSource(Mixed));
        using var cacheLifetime = cache;

        var report = await service.GetReportAsync(Ct.Token);

        Assert.Equal(TestData.T0, report.GeneratedAt);
        Assert.Equal(new[] { "Electronics", "Furniture" }, report.Categories.Select(c => c.Category));
        Assert.Equal(new[] { 20m, 100m }, report.Categories.Select(c => c.TotalInventoryValue));

        var s = report.Statistics;
        Assert.Equal(5, s.RecordsRetrieved);
        Assert.Equal(1, s.DuplicatesDiscarded);
        Assert.Equal(2, s.InvalidRecordsExcluded);
        Assert.Equal(1, s.ZeroQuantityRecordsExcluded);
        Assert.Equal(2, s.AvailableRecords);
        Assert.Equal(2, s.Warnings.Count);
    }

    [Fact]
    public async Task GetReportAsync_ReusesTheCachedReport_UntilItExpires()
    {
        var source = new FakeSource(Mixed);
        var (service, cache, clock) = Create(source);
        using var cacheLifetime = cache;

        var first = await service.GetReportAsync(Ct.Token);
        var second = await service.GetReportAsync(Ct.Token);
        clock.Advance(TimeSpan.FromMinutes(6));
        var third = await service.GetReportAsync(Ct.Token);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task GetReportAsync_LetsDataFailuresPropagate()
    {
        var source = new FakeSource(() => throw new InventoryDataException(InventoryDataFailure.Timeout, "slow"));
        var (service, cache, _) = Create(source);
        using var cacheLifetime = cache;

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => service.GetReportAsync(Ct.Token));

        Assert.Equal(InventoryDataFailure.Timeout, ex.Failure);
    }

    [Fact]
    public async Task GetReportAsync_CapsTheNumberOfWarningsButNotTheCount()
    {
        var bad = Enumerable.Range(1, 80).Select(i => (RawInventoryRecord?)TestData.Raw(i, price: -1m)).ToList();
        var (service, cache, _) = Create(new FakeSource(() => new RetrievedInventory(bad, 0)));
        using var cacheLifetime = cache;

        var report = await service.GetReportAsync(Ct.Token);

        Assert.Equal(80, report.Statistics.InvalidRecordsExcluded);
        Assert.Equal(50, report.Statistics.Warnings.Count);
        Assert.Empty(report.Categories);
    }
}
