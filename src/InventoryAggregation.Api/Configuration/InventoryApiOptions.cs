namespace InventoryAggregation.Api.Configuration;

/// <summary>
/// Settings for the upstream Inventory API, bound from the <c>InventoryApi</c> configuration section.
/// <see cref="BaseUrl"/> lives in appsettings.json. <see cref="ApiKey"/> is a secret: supply it through user-secrets
/// or the <c>InventoryApi__ApiKey</c> environment variable, never through a committed file.
/// </summary>
public sealed class InventoryApiOptions
{
    public const string SectionName = "InventoryApi";

    /// <summary>Base address of the Inventory API, e.g. <c>https://inventory.example.com/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Secret sent with every upstream request. Never logged.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>HTTP header that carries <see cref="ApiKey"/>.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>Page size to request (the upstream accepts 1-25). The server's echoed page size is authoritative.</summary>
    public int PageSize { get; set; } = 25;

    /// <summary>Maximum number of pages fetched in parallel.</summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>Safety valve: refuse to walk more pages than this, whatever the upstream claims.</summary>
    public int MaxPages { get; set; } = 1000;

    /// <summary>How many times one page is fetched when its <em>content</em> fails validation (HTTP retries are separate).</summary>
    public int MaxPageAttempts { get; set; } = 3;

    /// <summary>How long a successfully built report is reused. <see cref="TimeSpan.Zero"/> disables caching.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    public InventoryApiResilienceOptions Resilience { get; set; } = new();
}

/// <summary>HTTP-level resilience settings applied to the Inventory API client (read once at startup).</summary>
public sealed class InventoryApiResilienceOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public bool UseJitter { get; set; } = true;
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public double BreakerFailureRatio { get; set; } = 0.5;
    public int BreakerMinimumThroughput { get; set; } = 10;
    public TimeSpan BreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}
