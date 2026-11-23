using InventoryAggregation.Api.Infrastructure;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace InventoryAggregation.Tests.Inventory;

/// <summary>
/// The real HTTP pipeline - typed client, IHttpClientFactory, standard resilience handler - driven against a scripted
/// primary handler. Timings are shrunk to milliseconds through configuration; the behaviour under test is the production wiring.
/// </summary>
public class InventoryApiResilienceTests
{
    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Harness(
            Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder,
            IDictionary<string, string?>? overrides = null,
            bool includeResilienceSection = true)
        {
            var settings = new Dictionary<string, string?>
            {
                ["InventoryApi:BaseUrl"] = "https://inventory.test/",
                ["InventoryApi:ApiKey"] = "unit-test-key",
            };

            if (includeResilienceSection)
            {
                settings["InventoryApi:Resilience:MaxRetryAttempts"] = "2";
                settings["InventoryApi:Resilience:RetryDelay"] = "00:00:00.001";
                settings["InventoryApi:Resilience:UseJitter"] = "false";
                settings["InventoryApi:Resilience:AttemptTimeout"] = "00:00:01";
                settings["InventoryApi:Resilience:TotalTimeout"] = "00:00:30";
                settings["InventoryApi:Resilience:BreakerFailureRatio"] = "0.5";
                settings["InventoryApi:Resilience:BreakerMinimumThroughput"] = "100"; // effectively off unless a test lowers it
                settings["InventoryApi:Resilience:BreakerSamplingDuration"] = "00:00:30";
                settings["InventoryApi:Resilience:BreakerBreakDuration"] = "00:00:30";
            }

            foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            {
                settings[key] = value;
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            Handler = new StubHttpHandler(responder);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMetrics();
            services.AddInventoryApiClient(configuration).ConfigurePrimaryHttpMessageHandler(() => Handler);
            _provider = services.BuildServiceProvider(validateScopes: true);
        }

        public StubHttpHandler Handler { get; }

        public IInventoryPageClient Client => _provider.GetRequiredService<IInventoryPageClient>();

        public void Dispose() => _provider.Dispose();
    }

    private static Dictionary<string, string?> Resilience(string key, string value) => new() { [$"InventoryApi:Resilience:{key}"] = value };

    [Fact]
    public async Task SendsTheApiKeyAndAcceptHeaders_ToTheConfiguredBaseAddress()
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Ok()));

        await harness.Client.GetPageAsync(3, 25, Ct.Token);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal("https://inventory.test/api/records?page=3&pageSize=25", request.Uri.ToString());
        Assert.Equal("unit-test-key", request.Headers["X-Api-Key"]);
        Assert.Contains("application/json", request.Headers["Accept"]);
    }

    [Fact]
    public async Task KeepsAPathPrefixInTheBaseAddress()
    {
        using var harness = new Harness(
            StubHttpHandler.Script(Steps.Ok()),
            new Dictionary<string, string?> { ["InventoryApi:BaseUrl"] = "https://inventory.test/v1" });

        await harness.Client.GetPageAsync(1, 25, Ct.Token);

        Assert.Equal("https://inventory.test/v1/api/records?page=1&pageSize=25", harness.Handler.Requests.Single().Uri.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task RetriesTransientStatusCodes_UntilTheUpstreamRecovers(HttpStatusCode transient)
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Status(transient), Steps.Status(transient), Steps.Ok()));

        var page = await harness.Client.GetPageAsync(1, 25, Ct.Token);

        Assert.Equal(1, page.TotalRecords);
        Assert.Equal(3, harness.Handler.CallCount);   // 1 attempt + 2 retries
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredRetries_WithAnUnavailableFailure()
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Status(HttpStatusCode.InternalServerError)));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => harness.Client.GetPageAsync(1, 25, Ct.Token));

        Assert.Equal(InventoryDataFailure.Unavailable, ex.Failure);
        Assert.Equal(3, harness.Handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DoesNotRetryClientErrors(HttpStatusCode status)
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Status(status)));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => harness.Client.GetPageAsync(1, 25, Ct.Token));

        Assert.Equal(InventoryDataFailure.InvalidResponse, ex.Failure);
        Assert.Equal(1, harness.Handler.CallCount);
    }

    [Fact]
    public async Task RetriesTransportErrors()
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Throw(new HttpRequestException("reset by peer")), Steps.Ok()));

        await harness.Client.GetPageAsync(1, 25, Ct.Token);

        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task PersistentTransportErrors_BecomeUnavailable()
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Throw(new HttpRequestException("down"))));

        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => harness.Client.GetPageAsync(1, 25, Ct.Token));

        Assert.Equal(InventoryDataFailure.Unavailable, ex.Failure);
        Assert.Equal(3, harness.Handler.CallCount);
    }

    [Fact]
    public async Task AHungAttemptIsCutOffByTheAttemptTimeout_AndRetried()
    {
        using var harness = new Harness(
            StubHttpHandler.Script(Steps.Hang(), Steps.Ok()),
            Resilience("AttemptTimeout", "00:00:00.200"));

        await harness.Client.GetPageAsync(1, 25, Ct.Token);

        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task AnUpstreamThatNeverAnswers_BecomesATimeoutFailure()
    {
        using var harness = new Harness(
            StubHttpHandler.Script(Steps.Hang()),
            new Dictionary<string, string?>
            {
                ["InventoryApi:Resilience:AttemptTimeout"] = "00:00:00.200",
                ["InventoryApi:Resilience:MaxRetryAttempts"] = "1",
            });
        var ex = await Assert.ThrowsAsync<InventoryDataException>(() => harness.Client.GetPageAsync(1, 25, Ct.Token));

        Assert.Equal(InventoryDataFailure.Timeout, ex.Failure);
        Assert.Equal(2, harness.Handler.CallCount);
    }

    [Fact]
    public async Task TheCircuitOpensAfterRepeatedFailures_AndFailsFastWithoutCallingUpstream()
    {
        using var harness = new Harness(
            StubHttpHandler.Script(Steps.Status(HttpStatusCode.InternalServerError)),
            new Dictionary<string, string?>
            {
                ["InventoryApi:Resilience:MaxRetryAttempts"] = "1",
                ["InventoryApi:Resilience:BreakerMinimumThroughput"] = "2",
            });

        var first = await Assert.ThrowsAsync<InventoryDataException>(() => harness.Client.GetPageAsync(1, 25, Ct.Token));
        var callsAfterFirst = harness.Handler.CallCount;
        var second = await Assert.ThrowsAsync<InventoryDataException>(() => harness.Client.GetPageAsync(1, 25, Ct.Token));

        Assert.Equal(2, callsAfterFirst);                       // 1 attempt + 1 retry, both failures
        Assert.Equal(InventoryDataFailure.Unavailable, first.Failure);
        Assert.Equal(InventoryDataFailure.Unavailable, second.Failure);
        Assert.Equal(callsAfterFirst, harness.Handler.CallCount);   // circuit is open: no further upstream call
    }

    [Fact]
    public async Task FallsBackToDefaultResilienceSettings_WhenTheSectionIsAbsent()
    {
        using var harness = new Harness(StubHttpHandler.Script(Steps.Ok()), includeResilienceSection: false);

        var page = await harness.Client.GetPageAsync(1, 25, Ct.Token);

        Assert.Equal(1, page.TotalRecords);
    }

    [Fact]
    public void BuildsTheClientWithTheProductionPrimaryHandler_WhenNothingOverridesIt()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InventoryApi:BaseUrl"] = "https://inventory.test/",
                ["InventoryApi:ApiKey"] = "unit-test-key",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddInventoryApiClient(configuration);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var client = provider.GetRequiredService<IInventoryPageClient>();   // no network call is made

        Assert.NotNull(client);
    }
}
