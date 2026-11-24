using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Endpoints;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace InventoryAggregation.Tests.Endpoints;

public class InventoryEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class ProductionApiFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            base.ConfigureWebHost(builder);
        }
    }

    private sealed class NoApiKeyFactory : ApiFactory
    {
        protected override string ApiKey => "";
    }

    private static readonly RawInventoryRecord Printer =
        new(1001, "Thermal Receipt Printer", "Electronics", 149.99m, 12, new DateTimeOffset(2026, 10, 15, 12, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task SwaggerDocument_DescribesInventoryOperationResponsesAndSchema()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json", Ct.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct.Token));
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(InventoryEndpoints.CategoriesRoute)
            .GetProperty("get");

        Assert.Equal("Get inventory aggregated by category", operation.GetProperty("summary").GetString());
        Assert.Contains("No caller API key is required", operation.GetProperty("description").GetString());
        var responses = operation.GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.True(responses.TryGetProperty("502", out _));
        Assert.True(responses.TryGetProperty("503", out _));
        Assert.True(responses.TryGetProperty("504", out _));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Contains("Available inventory", schemas.GetProperty("CategoryResponse").GetProperty("description").GetString());
        Assert.Contains("unit price", schemas.GetProperty("ItemResponse").GetProperty("properties").GetProperty("price").GetProperty("description").GetString());
    }

    [Fact]
    public async Task SwaggerUi_IsServedInDevelopment()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/index.html", Ct.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Swagger UI", await response.Content.ReadAsStringAsync(Ct.Token));
    }

    [Fact]
    public async Task SwaggerEndpoints_AreNotServedOutsideDevelopment()
    {
        using var production = new ProductionApiFactory();
        using var client = production.CreateClient();

        using var documentResponse = await client.GetAsync("/swagger/v1/swagger.json", Ct.Token);
        using var uiResponse = await client.GetAsync("/swagger/index.html", Ct.Token);

        Assert.Equal(HttpStatusCode.NotFound, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, uiResponse.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsTheAggregatedCategories_InTheDocumentedShape()
    {
        factory.Behavior = () => Task.FromResult(new RetrievedInventory(
            [
                Printer,
                TestData.Raw(1002, "Electronics", 10m, 0),   // zero quantity: excluded
                TestData.Raw(2001, "Furniture", 200m, 1, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)),
                TestData.Raw(3001, "Toys", -1m, 1),           // invalid: excluded and reported
            ],
            0));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(InventoryEndpoints.CategoriesRoute, Ct.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (response.Content.Headers.ContentType is not null)
            Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct.Token);
        var categories = json.GetProperty("categories");
        Assert.Equal(2, categories.GetArrayLength());

        var electronics = categories[0];
        Assert.Equal("Electronics", electronics.GetProperty("category").GetString());
        Assert.Equal(1799.88m, electronics.GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(1, electronics.GetProperty("availableRecordCount").GetInt32());

        var mostRecent = electronics.GetProperty("mostRecentlyUpdated");
        Assert.Equal(1001, mostRecent.GetProperty("id").GetInt64());
        Assert.Equal("Thermal Receipt Printer", mostRecent.GetProperty("name").GetString());
        Assert.Equal("2026-10-15T12:30:00Z", mostRecent.GetProperty("lastUpdated").GetString());

        var item = Assert.Single(electronics.GetProperty("items").EnumerateArray());
        Assert.Equal(149.99m, item.GetProperty("price").GetDecimal());
        Assert.Equal(12, item.GetProperty("quantity").GetInt32());

        var summary = json.GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("recordsRetrieved").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalidRecordsExcluded").GetInt32());
        Assert.Equal(1, summary.GetProperty("zeroQuantityRecordsExcluded").GetInt32());
        Assert.Equal(2, summary.GetProperty("availableRecords").GetInt32());
        Assert.Equal(1, summary.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task Get_WithNoData_ReturnsAnEmptyList()
    {
        factory.Behavior = () => Task.FromResult(new RetrievedInventory([], 0));
        using var client = factory.CreateClient();

        var json = await client.GetFromJsonAsync<JsonElement>(InventoryEndpoints.CategoriesRoute, Ct.Token);

        Assert.Equal(0, json.GetProperty("categories").GetArrayLength());
    }

    [Theory]
    [InlineData(InventoryDataFailure.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(InventoryDataFailure.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData(InventoryDataFailure.InvalidResponse, HttpStatusCode.BadGateway)]
    public async Task Get_WhenUpstreamFails_ReturnsAProblemDetailsWithoutInternalDetail(InventoryDataFailure failure, HttpStatusCode expected)
    {
        factory.Behavior = () => throw new InventoryDataException(failure, "secret-upstream-host.internal returned HTTP 500");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(InventoryEndpoints.CategoriesRoute, Ct.Token);
        var body = await response.Content.ReadAsStringAsync(Ct.Token);

        Assert.Equal(expected, response.StatusCode);
        if (response.Content.Headers.ContentType is not null)
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType);
        Assert.Contains($"\"status\":{(int)expected}", body);
        Assert.DoesNotContain("secret-upstream-host", body);
    }

    [Fact]
    public async Task Get_WhenSomethingUnexpectedBreaks_StillReturnsAProblemDetails()
    {
        factory.Behavior = () => throw new InvalidOperationException("kaboom");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(InventoryEndpoints.CategoriesRoute, Ct.Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        if (response.Content.Headers.ContentType is not null)
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public async Task UnknownRoutes_Return404()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/nope", Ct.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [ExcludeFromCodeCoverage]
    public void Startup_FailsFast_WhenTheApiKeyIsNotConfigured()
    {
        using var misconfigured = new NoApiKeyFactory();

        var ex = Assert.ThrowsAny<Exception>(() => { _ = misconfigured.CreateClient(); });

        Assert.Contains("InventoryApi:ApiKey", ex.ToString());
    }

    [Fact]
    public async Task DefaultClientBehavior()
    {
        using var misconfigured = new NoApiKeyFactory();
        RetrievedInventory ri = await misconfigured.Behavior();
        Assert.NotNull(ri);
    }
}
