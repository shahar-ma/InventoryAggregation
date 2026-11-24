using Microsoft.Extensions.Options;

namespace InventoryAggregation.Api.Configuration;

/// <summary>
/// Fails fast at startup on missing or unsafe configuration (no API key, plain-http remote host, settings the resilience
/// pipeline would reject later at the first request).
/// </summary>
public sealed class InventoryApiOptionsValidator : IValidateOptions<InventoryApiOptions>
{
    /// <summary>Checks that the configured Inventory API settings are safe and usable.</summary>
    /// <param name="name">The name of the options instance being validated, if any.</param>
    /// <param name="options">The options instance to validate.</param>
    /// <returns>Success when all settings are valid; otherwise, the validation failures.</returns>
    public ValidateOptionsResult Validate(string? name, InventoryApiOptions options)
    {
        var failures = new List<string>();

        ValidateConnection(options, failures);
        ValidatePaging(options, failures);
        ValidateResilience(options.Resilience, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateConnection(InventoryApiOptions options, List<string> failures)
    {
        const string Prefix = InventoryApiOptions.SectionName;

        Require(
            Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) && IsPermittedScheme(baseUri),
            $"{Prefix}:BaseUrl must be an absolute https URL (http is only accepted for loopback hosts).",
            failures);
        Require(
            !string.IsNullOrWhiteSpace(options.ApiKey) && !options.ApiKey.Any(char.IsControl),
            $"{Prefix}:ApiKey is required and must not contain control characters. Set it with user-secrets or the InventoryApi__ApiKey environment variable.",
            failures);
        Require(
            !string.IsNullOrEmpty(options.ApiKeyHeaderName) && options.ApiKeyHeaderName.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-'),
            $"{Prefix}:ApiKeyHeaderName must be a non-empty header name made of letters, digits and '-'.",
            failures);
    }

    private static void ValidatePaging(InventoryApiOptions options, List<string> failures)
    {
        const string Prefix = InventoryApiOptions.SectionName;

        Require(options.PageSize is >= 1 and <= 25, $"{Prefix}:PageSize must be between 1 and 25.", failures);
        Require(options.MaxConcurrentRequests is >= 1 and <= 16, $"{Prefix}:MaxConcurrentRequests must be between 1 and 16.", failures);
        Require(options.MaxPages is >= 1 and <= 10_000, $"{Prefix}:MaxPages must be between 1 and 10000.", failures);
        Require(options.MaxPageAttempts is >= 1 and <= 10, $"{Prefix}:MaxPageAttempts must be between 1 and 10.", failures);
        Require(options.CacheDuration >= TimeSpan.Zero, $"{Prefix}:CacheDuration must not be negative.", failures);
    }

    private static void ValidateResilience(InventoryApiResilienceOptions options, List<string> failures)
    {
        const string Prefix = InventoryApiOptions.SectionName + ":Resilience";

        Require(options.MaxRetryAttempts is >= 1 and <= 10, $"{Prefix}:MaxRetryAttempts must be between 1 and 10.", failures);
        Require(options.RetryDelay > TimeSpan.Zero, $"{Prefix}:RetryDelay must be positive.", failures);
        Require(options.AttemptTimeout > TimeSpan.Zero, $"{Prefix}:AttemptTimeout must be positive.", failures);
        Require(options.TotalTimeout > options.AttemptTimeout, $"{Prefix}:TotalTimeout must be greater than AttemptTimeout.", failures);
        Require(options.BreakerFailureRatio is > 0d and <= 1d, $"{Prefix}:BreakerFailureRatio must be greater than 0 and at most 1.", failures);
        Require(options.BreakerMinimumThroughput >= 2, $"{Prefix}:BreakerMinimumThroughput must be at least 2.", failures);
        Require(
            options.BreakerSamplingDuration >= options.AttemptTimeout * 2,
            $"{Prefix}:BreakerSamplingDuration must be at least twice AttemptTimeout.",
            failures);
        Require(
            options.BreakerBreakDuration >= TimeSpan.FromMilliseconds(500),
            $"{Prefix}:BreakerBreakDuration must be at least 500 ms.",
            failures);
    }

    private static void Require(bool valid, string message, List<string> failures)
    {
        if (!valid)
        {
            failures.Add(message);
        }
    }

    private static bool IsPermittedScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
