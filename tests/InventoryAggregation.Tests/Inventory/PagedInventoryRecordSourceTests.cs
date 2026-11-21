using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Tests.Inventory;

public class PagedInventoryRecordSourceTests
{
    private static PagedInventoryRecordSource Create(IInventoryPageClient client, Action<InventoryApiOptions>? configure = null)
    {
        var options = new InventoryApiOptions
        {
            BaseUrl = "https://inventory.test/",
            ApiKey = "key",
            PageSize = 25,
            MaxConcurrentRequests = 4,
            MaxPages = 100,
            MaxPageAttempts = 3,
        };
        configure?.Invoke(options);
        return new PagedInventoryRecordSource(client, Options.Create(options), NullLogger<PagedInventoryRecordSource>.Instance);
    }

    private static FakePageClient Dataset(int total, int pageSizeServerUses = 0) =>
        new((page, requested, _) => TestData.DatasetPage(total, pageSizeServerUses > 0 ? pageSizeServerUses : requested, page));

    // ---- completeness ----

    [Fact]
    public async Task GetAllAsync_SinglePage_ReturnsEveryRecord()
    {
        var client = Dataset(10);

        var result = await Create(client).GetAllAsync(Ct.Token);

        Assert.Equal(10, result.Records.Count);
        Assert.Equal(0, result.DuplicatesDiscarded);
        Assert.Equal(new[] { (1, 25) }, client.Calls);
    }

    [Theory]
    [InlineData(57, 3)]   // last page partial
    [InlineData(50, 2)]   // last page exactly full
    [InlineData(26, 2)]   // one record spills onto page 2
    public async Task GetAllAsync_FetchesEveryPage_AndKeepsPageOrder(int total, int expectedPages)
    {
        var client = Dataset(total);

        var result = await Create(client).GetAllAsync(Ct.Token);

        Assert.Equal(total, result.Records.Count);
        Assert.Equal(Enumerable.Range(1, total).Select(i => (long?)i), result.Records.Select(r => r!.Id));
        Assert.Equal(Enumerable.Range(1, expectedPages), client.Calls.Select(c => c.Page).Order());
        Assert.All(client.Calls, c => Assert.Equal(25, c.PageSize));
    }

    [Fact]
    public async Task GetAllAsync_EmptyDataset_IsValidAndEmpty()
    {
        var client = Dataset(0);

        var result = await Create(client).GetAllAsync(Ct.Token);

        Assert.Empty(result.Records);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task GetAllAsync_WhenServerClampsPageSize_UsesTheServersSizeForLaterPages()
    {
        var client = Dataset(45, pageSizeServerUses: 20);

        var result = await Create(client).GetAllAsync(Ct.Token);

        Assert.Equal(45, result.Records.Count);
        Assert.Equal(25, client.Calls.Single(c => c.Page == 1).PageSize);
        Assert.All(client.Calls.Where(c => c.Page > 1), c => Assert.Equal(20, c.PageSize));
    }

    [Fact]
    public async Task GetAllAsync_RespectsTheConcurrencyLimit()
    {
        var client = new FakePageClient((page, size, _) => TestData.DatasetPage(200, size, page), TimeSpan.FromMilliseconds(15));

        await Create(client, o => o.MaxConcurrentRequests = 2).GetAllAsync(Ct.Token);

        Assert.Equal(8, client.Calls.Count);
        Assert.InRange(client.MaxInFlight, 1, 2);
    }

    // ---- integrity: first page ----

    [Theory]
    [InlineData("wrong-page", "requested page 1")]
    [InlineData("negative-records", "negative total")]
    [InlineData("negative-pages", "negative total")]
    [InlineData("zero-page-size", "page size 0")]
    [InlineData("oversized-page", "page size 30")]
    [InlineData("inconsistent-pages", "does not match")]
    [InlineData("short-page", "expected 10 records but received 9")]
    public async Task GetAllAsync_RejectsAnInconsistentFirstPage(string scenario, string expectedFragment)
    {
        InventoryPage Build() => scenario switch
        {
            "wrong-page" => TestData.DatasetPage(60, 25, 2),
            "negative-records" => TestData.Page(1, 25, 1, -1),
            "negative-pages" => TestData.Page(1, 25, -1, 5),
            "zero-page-size" => TestData.Page(1, 0, 1, 5),
            "oversized-page" => TestData.Page(1, 30, 1, 5),
            "inconsistent-pages" => TestData.Page(1, 25, 3, 10, TestData.Records(10)),
            "short-page" => TestData.Page(1, 25, 1, 10, TestData.Records(9)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };
        var client = new FakePageClient((_, _, _) => Build());

        var ex = await Assert.ThrowsAsync<InventoryDataException>(
            () => Create(client, o => o.MaxPageAttempts = 1).GetAllAsync(Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
        Assert.Contains(expectedFragment, ex.Message);
    }

    // ---- integrity: later pages ----

    [Theory]
    [InlineData("page-size-changed", "page size changed")]
    [InlineData("total-records-changed", "totals changed")]
    [InlineData("total-pages-changed", "totals changed")]
    public async Task GetAllAsync_RejectsPagesThatContradictTheFirstPage(string scenario, string expectedFragment)
    {
        InventoryPage Altered() => scenario switch
        {
            "page-size-changed" => TestData.DatasetPage(60, 20, 2),
            "total-records-changed" => TestData.Page(2, 25, 3, 61, TestData.Records(25)),
            "total-pages-changed" => TestData.Page(2, 25, 4, 60, TestData.Records(25)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };
        var client = new FakePageClient((page, _, _) => page == 2 ? Altered() : TestData.DatasetPage(60, 25, page));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(
            () => Create(client, o => o.MaxPageAttempts = 1).GetAllAsync(Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public async Task GetAllAsync_RetriesAPageWhoseContentIsShort_ThenSucceeds()
    {
        var client = new FakePageClient((page, size, attempt) =>
        {
            var good = TestData.DatasetPage(60, size, page);
            return page == 2 && attempt == 1
                ? TestData.Page(2, good.PageSize, good.TotalPages, good.TotalRecords, good.Records.Take(good.Records.Count - 1).ToArray())
                : good;
        });

        var result = await Create(client).GetAllAsync(Ct.Token);

        Assert.Equal(60, result.Records.Count);
        Assert.Equal(2, client.Calls.Count(c => c.Page == 2));
    }

    [Fact]
    public async Task GetAllAsync_GivesUpOnAPageThatStaysInvalid()
    {
        var client = new FakePageClient((page, size, _) =>
        {
            var good = TestData.DatasetPage(60, size, page);
            return page == 2
                ? TestData.Page(2, good.PageSize, good.TotalPages, good.TotalRecords, good.Records.Take(1).ToArray())
                : good;
        });

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Create(client).GetAllAsync(Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
        Assert.Contains("Page 2 failed validation after 3 attempt(s)", ex.Message);
        Assert.Equal(3, client.Calls.Count(c => c.Page == 2));
    }

    [Fact]
    public async Task GetAllAsync_RefusesToWalkMorePagesThanTheSafetyLimit()
    {
        var client = Dataset(250);

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Create(client, o => o.MaxPages = 5).GetAllAsync(Ct.Token));

        Assert.Contains("safety limit of 5", ex.Message);
        Assert.Single(client.Calls);
    }

    // ---- duplicates and odd records ----

    [Fact]
    public async Task GetAllAsync_DiscardsIdenticalDuplicates_AndCountsThem()
    {
        var (r1, r2, r3) = (TestData.Raw(1), TestData.Raw(2), TestData.Raw(3));
        var client = new FakePageClient((page, _, _) => page == 1
            ? TestData.Page(1, 2, 2, 4, r1, r2)
            : TestData.Page(2, 2, 2, 4, r2, r3));

        var result = await Create(client, o => o.PageSize = 2).GetAllAsync(Ct.Token);

        Assert.Equal(new RawInventoryRecord?[] { r1, r2, r3 }, result.Records);
        Assert.Equal(1, result.DuplicatesDiscarded);
    }

    [Fact]
    public async Task GetAllAsync_FailsWhenTheSameIdCarriesConflictingContent()
    {
        var client = new FakePageClient((page, _, _) => page == 1
            ? TestData.Page(1, 2, 2, 4, TestData.Raw(1), TestData.Raw(2))
            : TestData.Page(2, 2, 2, 4, TestData.Raw(2, price: 99m), TestData.Raw(3)));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Create(client, o => o.PageSize = 2).GetAllAsync(Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
        Assert.Contains("conflicting records for id 2", ex.Message);
    }

    [Fact]
    public async Task GetAllAsync_PassesThroughRecordsItCannotDeduplicate()
    {
        var noId = new RawInventoryRecord(null, "No id", "Cat", 1m, 1, TestData.T0);
        var client = new FakePageClient((_, _, _) => TestData.Page(1, 3, 1, 3, TestData.Raw(1), null, noId));

        var result = await Create(client, o => o.PageSize = 3).GetAllAsync(Ct.Token);

        Assert.Equal(3, result.Records.Count);
        Assert.Null(result.Records[1]);
        Assert.Equal(0, result.DuplicatesDiscarded);
    }

    // ---- failures from below, cancellation ----

    [Fact]
    public async Task GetAllAsync_PropagatesClientFailures()
    {
        var client = new FakePageClient((page, size, _) => page == 2
            ? throw new InventoryDataException(InventoryDataFailure.Unavailable, "boom")
            : TestData.DatasetPage(60, size, page));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Create(client).GetAllAsync(Ct.Token));

        Assert.Equal(InventoryDataFailure.Unavailable, ex.Failure);
    }

    [Fact]
    public async Task GetAllAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(Dataset(10)).GetAllAsync(cts.Token));
    }
}
