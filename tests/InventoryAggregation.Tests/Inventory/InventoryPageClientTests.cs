using System.Net;
using System.Text;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace InventoryAggregation.Tests.Inventory;

/// <summary>The client in isolation (no resilience pipeline): how each kind of upstream outcome is translated.</summary>
public class InventoryPageClientTests
{
    private static (InventoryPageClient Client, StubHttpHandler Handler) Create(
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        var handler = new StubHttpHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://inventory.test/") };
        return (new InventoryPageClient(http, NullLogger<InventoryPageClient>.Instance), handler);
    }

    private static Task<InventoryPage> Get(InventoryPageClient client, CancellationToken token = default) =>
        client.GetPageAsync(1, 25, token);

    [Fact]
    public async Task GetPageAsync_ParsesTheEnvelopeAndRecords_AndBuildsTheRequestUri()
    {
        var body = TestData.PageJson(
            2, 25, 3, 52,
            TestData.RecordJson(1001),
            new { id = 1002, name = (string?)null, category = "Electronics", price = 1.5m, quantity = 2, lastUpdated = "2026-10-15T12:30:00Z" });
        var (client, handler) = Create((_, _, _) => Task.FromResult(StubHttpHandler.Json(body)));

        var page = await client.GetPageAsync(2, 25, Ct.Token);

        Assert.Equal(("https://inventory.test/api/records?page=2&pageSize=25"), handler.Requests.Single().Uri.ToString());
        Assert.Equal((2, 25, 3, 52, 2), (page.Page, page.PageSize, page.TotalPages, page.TotalRecords, page.Records.Count));
        var first = page.Records[0]!;
        Assert.Equal((1001L, "Item 1001", "Electronics", 149.99m, 12), (first.Id!.Value, first.Name, first.Category, first.Price!.Value, first.Quantity!.Value));
        Assert.Equal(TestData.T0, first.LastUpdated);
        Assert.Null(page.Records[1]!.Name);   // a missing field stays visible instead of becoming a default
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"records":[],"pageSize":25,"totalPages":1,"totalRecords":0}""")]
    [InlineData("""{"records":[],"page":1,"totalPages":1,"totalRecords":0}""")]
    [InlineData("""{"records":[],"page":1,"pageSize":25,"totalRecords":0}""")]
    [InlineData("""{"records":[],"page":1,"pageSize":25,"totalPages":1}""")]
    [InlineData("""{"page":1,"pageSize":25,"totalPages":1,"totalRecords":0}""")]
    public async Task GetPageAsync_RejectsAnIncompleteEnvelope(string body)
    {
        await GetPageAsync_Rejects(body);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"records":[],"page":"one","pageSize":25,"totalPages":1,"totalRecords":0}""")]
    public async Task GetPageAsync_RejectsAMalformedBody(string body)
    {
        await GetPageAsync_Rejects(body);
    }

    [Fact]
    public async Task GetPageAsync_RejectsANonJsonContentType()
    {
        var (client, _) = Create((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html/>", Encoding.UTF8, "text/html") }));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, InventoryDataFailure.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, InventoryDataFailure.Unavailable)]
    [InlineData(HttpStatusCode.RequestTimeout, InventoryDataFailure.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, InventoryDataFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, InventoryDataFailure.InvalidResponse)]
    [InlineData(HttpStatusCode.Unauthorized, InventoryDataFailure.InvalidResponse)]
    [InlineData(HttpStatusCode.NotFound, InventoryDataFailure.InvalidResponse)]
    [InlineData(HttpStatusCode.Found, InventoryDataFailure.InvalidResponse)]
    public async Task GetPageAsync_MapsNonSuccessStatusCodes(HttpStatusCode status, InventoryDataFailure expected)
    {
        var (client, _) = Create((_, _, _) => Task.FromResult(new HttpResponseMessage(status)));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(expected, ex.Failure);
        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    [Fact]
    public async Task GetPageAsync_TransportErrors_BecomeUnavailable()
    {
        var inner = new HttpRequestException("connection refused");
        var (client, _) = Create((_, _, _) => throw inner);

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(InventoryDataFailure.Unavailable, ex.Failure);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task GetPageAsync_PipelineTimeouts_BecomeTimeout()
    {
        var (client, _) = Create((_, _, _) => throw new TimeoutRejectedException("timed out"));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(InventoryDataFailure.Timeout, ex.Failure);
    }

    [Fact]
    public async Task GetPageAsync_OtherPipelineRejections_BecomeUnavailable()
    {
        var (client, _) = Create((_, _, _) => throw new BrokenCircuitException("open"));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(InventoryDataFailure.Unavailable, ex.Failure);
    }

    [Fact]
    public async Task GetPageAsync_CancellationThatIsNotTheCallers_BecomesTimeout()
    {
        var (client, _) = Create((_, _, _) => throw new TaskCanceledException("handler timeout"));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(InventoryDataFailure.Timeout, ex.Failure);
    }

    [Fact]
    public async Task GetPageAsync_CallerCancellation_IsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (client, _) = Create(StubHttpHandler.Script(Steps.Ok()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Get(client, cts.Token));
    }

    private static async Task GetPageAsync_Rejects(string body)
    {
        var (client, _) = Create((_, _, _) => Task.FromResult(StubHttpHandler.Json(body)));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => Get(client, Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
    }
}
