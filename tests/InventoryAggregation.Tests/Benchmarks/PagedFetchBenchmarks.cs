using BenchmarkDotNet.Attributes;
using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Tests.Benchmarks;

/// <summary>
/// Pagination cost against a simulated 2 ms-per-page upstream (40 pages): shows what the bounded parallelism buys over
/// fetching sequentially.
/// </summary>
[MemoryDiagnoser]
public class PagedFetchBenchmarks
{
    public const int TotalRecords = 25 * 40;

    private PagedInventoryRecordSource _source = null!;

    [Params(1, 4, 8)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var client = new FakePageClient(
            (page, size, _) => TestData.DatasetPage(TotalRecords, size, page),
            latency: TimeSpan.FromMilliseconds(2));
        var options = Options.Create(new InventoryApiOptions
        {
            BaseUrl = "https://inventory.test/",
            ApiKey = "benchmark",
            MaxConcurrentRequests = Concurrency,
        });
        _source = new PagedInventoryRecordSource(client, options, NullLogger<PagedInventoryRecordSource>.Instance);
    }

    [Benchmark]
    public async Task<int> FetchAllPages() => (await _source.GetAllAsync(CancellationToken.None)).Records.Count;
}
