using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Domain;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Api.Reporting;

/// <summary>
/// Single-entry, single-flight cache for the report. The dataset is static, so rebuilding it (a dozen upstream calls) for
/// every inbound request would be wasteful, and N simultaneous callers must not trigger N simultaneous rebuilds. Failures
/// are never cached. A <see cref="CacheDuration"/> of zero disables caching.
/// </summary>
public sealed class ReportCache(IOptions<InventoryApiOptions> options, TimeProvider clock) : IDisposable
{
    private sealed record Entry(InventoryReport Report, DateTimeOffset ExpiresAt);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    /// <summary>Returns a fresh cached report or builds and caches one report for concurrent callers.</summary>
    /// <param name="factory">Creates a report when no fresh cached report is available.</param>
    /// <param name="cancellationToken">Cancels waiting for the cache or building the report.</param>
    /// <returns>The cached or newly created report.</returns>
    /// <exception cref="ObjectDisposedException">The current instance has already been disposed.</exception>
    /// <exception cref="OperationCanceledException"></exception>
    public async Task<InventoryReport> GetOrCreateAsync(
        Func<CancellationToken, Task<InventoryReport>> factory,
        CancellationToken cancellationToken)
    {
        if (GetFresh() is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have rebuilt the report while this one waited for the gate.
            if (GetFresh() is { } rebuilt)
            {
                return rebuilt;
            }

            var report = await factory(cancellationToken).ConfigureAwait(false);

            var ttl = options.Value.CacheDuration;
            if (ttl > TimeSpan.Zero)
            {
                _entry = new Entry(report, clock.GetUtcNow() + ttl);
            }

            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the semaphore used to coordinate report creation.</summary>
    public void Dispose() => _gate.Dispose();

    private InventoryReport? GetFresh()
    {
        var entry = _entry;
        return entry is not null && entry.ExpiresAt > clock.GetUtcNow() ? entry.Report : null;
    }
}
