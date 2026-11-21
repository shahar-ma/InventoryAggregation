using System.Net;
using System.Net.Http.Headers;
using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Api.Reporting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace InventoryAggregation.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    private const int MaxResponseBytes = 1_048_576;

    /// <summary>Registers everything the service needs. The composition root is the only place that knows all the pieces.</summary>
    public static IServiceCollection AddInventoryAggregation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInventoryApiClient(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IInventoryAggregator, InventoryAggregator>();
        services.AddSingleton<ReportCache>();
        services.AddTransient<IInventoryRecordSource, PagedInventoryRecordSource>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();

        return services;
    }

    /// <summary>
    /// Registers validated options and the typed <see cref="IInventoryPageClient"/>, created through <c>IHttpClientFactory</c>
    /// and wrapped in the standard resilience pipeline (rate limiter, total timeout, retry, circuit breaker, attempt timeout).
    /// Resilience settings are read once, here; everything else is read through <see cref="IOptions{TOptions}"/>.
    /// </summary>
    public static IHttpClientBuilder AddInventoryApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(InventoryApiOptions.SectionName);

        services.AddOptions<InventoryApiOptions>().Bind(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InventoryApiOptions>, InventoryApiOptionsValidator>());

        var resilience = section.GetSection(nameof(InventoryApiOptions.Resilience)).Get<InventoryApiResilienceOptions>()
            ?? new InventoryApiResilienceOptions();

        var builder = services
            .AddHttpClient<IInventoryPageClient, InventoryPageClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);

        builder.AddStandardResilienceHandler(options => ConfigureResilience(options, resilience));

        return builder;
    }

    private static void ConfigureClient(IServiceProvider services, HttpClient client)
    {
        var options = services.GetRequiredService<IOptions<InventoryApiOptions>>().Value;

        // Trailing slash so relative request URIs append to (rather than replace) any path in the base address.
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.DefaultRequestHeaders.Add(options.ApiKeyHeaderName, options.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // The resilience pipeline owns timeouts; HttpClient's own 100 s default would race with it.
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
    }

    private static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        // Never follow redirects: the API key header must not be forwarded to another host.
        AllowAutoRedirect = false,
        // Refresh DNS regularly even though the typed client may be held for a while.
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    private static void ConfigureResilience(HttpStandardResilienceOptions options, InventoryApiResilienceOptions settings)
    {
        options.Retry.MaxRetryAttempts = settings.MaxRetryAttempts;
        options.Retry.Delay = settings.RetryDelay;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = settings.UseJitter;

        options.AttemptTimeout.Timeout = settings.AttemptTimeout;
        options.TotalRequestTimeout.Timeout = settings.TotalTimeout;

        options.CircuitBreaker.FailureRatio = settings.BreakerFailureRatio;
        options.CircuitBreaker.MinimumThroughput = settings.BreakerMinimumThroughput;
        options.CircuitBreaker.SamplingDuration = settings.BreakerSamplingDuration;
        options.CircuitBreaker.BreakDuration = settings.BreakerBreakDuration;
    }
}
