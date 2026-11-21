using InventoryAggregation.Api;
using InventoryAggregation.Api.Inventory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryAggregation.Tests.Endpoints;

/// <summary>
/// Hosts the real application (real pipeline, DI, validation, aggregation, caching) and replaces only the outermost
/// dependency - the record source - with a delegate, so tests control what "upstream" produced.
/// </summary>
public class ApiFactory : WebApplicationFactory<ApiMarker>
{
    /// <summary>What the stand-in record source does. Tests replace this per scenario (tests in one class run sequentially).</summary>
    public Func<Task<RetrievedInventory>> Behavior { get; set; } = () => Task.FromResult(new RetrievedInventory([], 0));

    protected virtual string ApiKey => "test-api-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InventoryApi:BaseUrl"] = "https://inventory.test/",
            ["InventoryApi:ApiKey"] = ApiKey,
            ["InventoryApi:CacheDuration"] = "00:00:00",   // no caching between scenarios
        }));

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IInventoryRecordSource>(new DelegatingSource(() => Behavior())));
    }

    private sealed class DelegatingSource(Func<Task<RetrievedInventory>> behavior) : IInventoryRecordSource
    {
        public Task<RetrievedInventory> GetAllAsync(CancellationToken cancellationToken) => behavior();
    }
}
