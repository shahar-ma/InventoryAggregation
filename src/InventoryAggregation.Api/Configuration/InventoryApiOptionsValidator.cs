using Microsoft.Extensions.Options;

namespace InventoryAggregation.Api.Configuration;

/// <summary>
/// Fails fast at startup on missing or unsafe configuration (no API key, plain-http remote host, settings the resilience
/// pipeline would reject later at the first request).
/// </summary>
public sealed class InventoryApiOptionsValidator : IValidateOptions<InventoryApiOptions>
{
    public ValidateOptionsResult Validate(string? name, InventoryApiOptions options)
    {
        var failures = new List<string>();
        var resilience = options.Resilience;
        const string Prefix = InventoryApiOptions.SectionName;

        Require(
            Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) && IsPermittedScheme(baseUri),
            $"{Prefix}:BaseUrl must be an absolute https URL (http is only accepted for loopback hosts).");
        Require(
            !string.IsNullOrWhiteSpace(options.ApiKey) && !options.ApiKey.Any(char.IsControl),
            $"{Prefix}:ApiKey is required and must not contain control characters. Set it with user-secrets or the InventoryApi__ApiKey environment variable.");
        Require(
            !string.IsNullOrEmpty(options.ApiKeyHeaderName) && options.ApiKeyHeaderName.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-'),
            $"{Prefix}:ApiKeyHeaderName must be a non-empty header name made of letters, digits and '-'.");
        Require(options.PageSize is >= 1 and <= 25, $"{Prefix}:PageSize must be between 1 and 25.");
        Require(options.MaxConcurrentRequests is >= 1 and <= 16, $"{Prefix}:MaxConcurrentRequests must be between 1 and 16.");
        Require(options.MaxPages is >= 1 and <= 10_000, $"{Prefix}:MaxPages must be between 1 and 10000.");
        Require(options.MaxPageAttempts is >= 1 and <= 10, $"{Prefix}:MaxPageAttempts must be between 1 and 10.");
        Require(options.CacheDuration >= TimeSpan.Zero, $"{Prefix}:CacheDuration must not be negative.");

        Require(resilience.MaxRetryAttempts is >= 1 and <= 10, $"{Prefix}:Resilience:MaxRetryAttempts must be between 1 and 10.");
        Require(resilience.RetryDelay > TimeSpan.Zero, $"{Prefix}:Resilience:RetryDelay must be positive.");
        Require(resilience.AttemptTimeout > TimeSpan.Zero, $"{Prefix}:Resilience:AttemptTimeout must be positive.");
        Require(resilience.TotalTimeout > resilience.AttemptTimeout, $"{Prefix}:Resilience:TotalTimeout must be greater than AttemptTimeout.");
        Require(resilience.BreakerFailureRatio is > 0d and <= 1d, $"{Prefix}:Resilience:BreakerFailureRatio must be greater than 0 and at most 1.");
        Require(resilience.BreakerMinimumThroughput >= 2, $"{Prefix}:Resilience:BreakerMinimumThroughput must be at least 2.");
        Require(
            resilience.BreakerSamplingDuration >= resilience.AttemptTimeout * 2,
            $"{Prefix}:Resilience:BreakerSamplingDuration must be at least twice AttemptTimeout.");
        Require(
            resilience.BreakerBreakDuration >= TimeSpan.FromMilliseconds(500),
            $"{Prefix}:Resilience:BreakerBreakDuration must be at least 500 ms.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void Require(bool valid, string message)
        {
            if (!valid)
            {
                failures.Add(message);
            }
        }
    }

    private static bool IsPermittedScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
