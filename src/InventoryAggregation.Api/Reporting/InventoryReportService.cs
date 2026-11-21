using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Inventory;

namespace InventoryAggregation.Api.Reporting;

/// <summary>
/// Orchestrates retrieve -> sanitize -> aggregate and assembles the statistics. Scoped, so each report build resolves
/// a fresh typed HTTP client from the factory; only the finished report is kept (in the singleton <see cref="ReportCache"/>).
/// </summary>
public sealed class InventoryReportService(
    IInventoryRecordSource source,
    IInventoryAggregator aggregator,
    ReportCache cache,
    TimeProvider clock) : IInventoryReportService
{
    private const int MaxWarnings = 50;

    public Task<InventoryReport> GetReportAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(BuildReportAsync, cancellationToken);

    private async Task<InventoryReport> BuildReportAsync(CancellationToken cancellationToken)
    {
        var retrieved = await source.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var sanitized = RecordSanitizer.Sanitize(retrieved.Records);
        var aggregation = aggregator.Aggregate(sanitized.Records);

        var statistics = new ReportStatistics(
            RecordsRetrieved: retrieved.Records.Count,
            DuplicatesDiscarded: retrieved.DuplicatesDiscarded,
            InvalidRecordsExcluded: sanitized.Problems.Count,
            ZeroQuantityRecordsExcluded: aggregation.ZeroQuantityExcluded,
            AvailableRecords: aggregation.Categories.Sum(static c => c.AvailableRecordCount),
            Warnings: sanitized.Problems.Take(MaxWarnings).ToList());

        return new InventoryReport(clock.GetUtcNow(), aggregation.Categories, statistics);
    }
}
