using InventoryAggregation.Api.Configuration;

namespace InventoryAggregation.Tests.Configuration;

public class InventoryApiOptionsValidatorTests
{
    private readonly InventoryApiOptionsValidator _sut = new();

    private static InventoryApiOptions Valid() => new() { BaseUrl = "https://inventory.example.com/", ApiKey = "secret" };

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
        Assert.Contains(result.Failures!, failure => failure.Contains(expectedFragment));
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var options = new InventoryApiOptions { BaseUrl = "", ApiKey = "" };

        var result = _sut.Validate(null, options);

        Assert.Equal(2, result.Failures!.Count());
    }

    private static void Apply(string scenario, InventoryApiOptions o)
    {
        switch (scenario)
        {
            case "base-url-empty": o.BaseUrl = ""; break;
            case "base-url-relative": o.BaseUrl = "/api"; break;
            case "base-url-http-remote": o.BaseUrl = "http://inventory.example.com/"; break;
            case "base-url-ftp": o.BaseUrl = "ftp://localhost/"; break;
            case "api-key-empty": o.ApiKey = "  "; break;
            case "api-key-control-char": o.ApiKey = "abc\ndef"; break;
            case "header-empty": o.ApiKeyHeaderName = ""; break;
            case "header-invalid": o.ApiKeyHeaderName = "X Api Key"; break;
            case "page-size-low": o.PageSize = 0; break;
            case "page-size-high": o.PageSize = 26; break;
            case "concurrency-low": o.MaxConcurrentRequests = 0; break;
            case "concurrency-high": o.MaxConcurrentRequests = 17; break;
            case "max-pages-low": o.MaxPages = 0; break;
            case "max-pages-high": o.MaxPages = 10_001; break;
            case "page-attempts-low": o.MaxPageAttempts = 0; break;
            case "page-attempts-high": o.MaxPageAttempts = 11; break;
            case "cache-negative": o.CacheDuration = TimeSpan.FromSeconds(-1); break;
            case "retries-low": o.Resilience.MaxRetryAttempts = 0; break;
            case "retries-high": o.Resilience.MaxRetryAttempts = 11; break;
            case "retry-delay-zero": o.Resilience.RetryDelay = TimeSpan.Zero; break;
            case "attempt-timeout-zero": o.Resilience.AttemptTimeout = TimeSpan.Zero; break;
            case "total-timeout-not-greater": o.Resilience.TotalTimeout = o.Resilience.AttemptTimeout; break;
            case "ratio-zero": o.Resilience.BreakerFailureRatio = 0; break;
            case "ratio-high": o.Resilience.BreakerFailureRatio = 1.5; break;
            case "throughput-low": o.Resilience.BreakerMinimumThroughput = 1; break;
            case "sampling-too-short": o.Resilience.BreakerSamplingDuration = o.Resilience.AttemptTimeout; break;
            case "break-too-short": o.Resilience.BreakerBreakDuration = TimeSpan.FromMilliseconds(100); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }
    }
}
