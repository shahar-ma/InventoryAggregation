using InventoryAggregation.Api.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace InventoryAggregation.Tests.Configuration;

public class InventoryApiOptionsValidatorTests
{
    private readonly InventoryApiOptionsValidator _sut = new();

    private static InventoryApiOptions Valid() => new() { BaseUrl = "https://inventory.example.com/", ApiKey = "secret" };

    private static readonly Dictionary<string, Action<InventoryApiOptions>> ScenarioMutations =
        new()
        {
            ["api-key-control-char"] = o => o.ApiKey = "abc\ndef",
            ["api-key-empty"] = o => o.ApiKey = "  ",
            ["attempt-timeout-zero"] = o => o.Resilience.AttemptTimeout = TimeSpan.Zero,
            ["base-url-empty"] = o => o.BaseUrl = "",
            ["base-url-ftp"] = o => o.BaseUrl = "ftp://localhost/",
            ["base-url-http-remote"] = o => o.BaseUrl = "http://inventory.example.com/",
            ["base-url-relative"] = o => o.BaseUrl = "/api",
            ["break-too-short"] = o => o.Resilience.BreakerBreakDuration = TimeSpan.FromMilliseconds(100),
            ["cache-negative"] = o => o.CacheDuration = TimeSpan.FromSeconds(-1),
            ["concurrency-high"] = o => o.MaxConcurrentRequests = 17,
            ["concurrency-low"] = o => o.MaxConcurrentRequests = 0,
            ["header-empty"] = o => o.ApiKeyHeaderName = "",
            ["header-invalid"] = o => o.ApiKeyHeaderName = "X Api Key",
            ["max-pages-high"] = o => o.MaxPages = 10_001,
            ["max-pages-low"] = o => o.MaxPages = 0,
            ["page-attempts-high"] = o => o.MaxPageAttempts = 11,
            ["page-attempts-low"] = o => o.MaxPageAttempts = 0,
            ["page-size-high"] = o => o.PageSize = 26,
            ["page-size-low"] = o => o.PageSize = 0,
            ["ratio-high"] = o => o.Resilience.BreakerFailureRatio = 1.5,
            ["ratio-zero"] = o => o.Resilience.BreakerFailureRatio = 0,
            ["retries-high"] = o => o.Resilience.MaxRetryAttempts = 11,
            ["retries-low"] = o => o.Resilience.MaxRetryAttempts = 0,
            ["retry-delay-zero"] = o => o.Resilience.RetryDelay = TimeSpan.Zero,
            ["sampling-too-short"] = o => o.Resilience.BreakerSamplingDuration = o.Resilience.AttemptTimeout,
            ["throughput-low"] = o => o.Resilience.BreakerMinimumThroughput = 1,
            ["total-timeout-not-greater"] = o => o.Resilience.TotalTimeout = o.Resilience.AttemptTimeout,
        };

    [Fact]
    public void Validate_AcceptsTheDefaultsPlusABaseUrlAndKey() =>
        Assert.True(_sut.Validate(null, Valid()).Succeeded);

    [Theory]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1/")]
    public void Validate_AllowsPlainHttpForLoopbackHosts(string baseUrl)
    {
        var options = Valid();
        options.BaseUrl = baseUrl;

        Assert.True(_sut.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_AllowsCachingToBeDisabledWithZero()
    {
        var options = Valid();
        options.CacheDuration = TimeSpan.Zero;

        Assert.True(_sut.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("base-url-empty", "InventoryApi:BaseUrl")]
    [InlineData("base-url-relative", "InventoryApi:BaseUrl")]
    [InlineData("base-url-http-remote", "InventoryApi:BaseUrl")]
    [InlineData("base-url-ftp", "InventoryApi:BaseUrl")]
    [InlineData("api-key-empty", "InventoryApi:ApiKey")]
    [InlineData("api-key-control-char", "InventoryApi:ApiKey")]
    [InlineData("header-empty", "ApiKeyHeaderName")]
    [InlineData("header-invalid", "ApiKeyHeaderName")]
    [InlineData("page-size-low", "PageSize")]
    [InlineData("page-size-high", "PageSize")]
    [InlineData("concurrency-low", "MaxConcurrentRequests")]
    [InlineData("concurrency-high", "MaxConcurrentRequests")]
    [InlineData("max-pages-low", "MaxPages")]
    [InlineData("max-pages-high", "MaxPages")]
    [InlineData("page-attempts-low", "MaxPageAttempts")]
    [InlineData("page-attempts-high", "MaxPageAttempts")]
    [InlineData("cache-negative", "CacheDuration")]
    [InlineData("retries-low", "MaxRetryAttempts")]
    [InlineData("retries-high", "MaxRetryAttempts")]
    [InlineData("retry-delay-zero", "RetryDelay")]
    [InlineData("attempt-timeout-zero", "AttemptTimeout must be positive")]
    [InlineData("total-timeout-not-greater", "TotalTimeout")]
    [InlineData("ratio-zero", "BreakerFailureRatio")]
    [InlineData("ratio-high", "BreakerFailureRatio")]
    [InlineData("throughput-low", "BreakerMinimumThroughput")]
    [InlineData("sampling-too-short", "BreakerSamplingDuration")]
    [InlineData("break-too-short", "BreakerBreakDuration")]
    public void Validate_RejectsUnsafeOrUnusableSettings(string scenario, string expectedFragment)
    {
        var options = Valid();
        Apply(scenario, options);

        var result = _sut.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains(expectedFragment));
    }

    [Fact]
    [ExcludeFromCodeCoverage]
    public void Validate_RejectsUnsafeOrUnusableSettingsThrows()
    {
        var options = Valid();
        Assert.Throws<ArgumentOutOfRangeException>(() => Apply("Invalid", options));
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var options = new InventoryApiOptions { BaseUrl = "", ApiKey = "" };

        var result = _sut.Validate(null, options);

        Assert.Equal(2, result.Failures!.Count());
    }

    private static void Apply(string scenario, InventoryApiOptions options)
    {
        if (ScenarioMutations.TryGetValue(scenario, out var mutate))
        {
            mutate(options);
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
    }

}
