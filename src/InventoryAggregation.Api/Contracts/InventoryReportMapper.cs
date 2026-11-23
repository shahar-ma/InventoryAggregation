using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Contracts;

/// <summary>Maps the domain report to the public contract so the two can evolve independently.</summary>
public static class InventoryReportMapper
{
    /// <summary>Creates the API response representation of an inventory report.</summary>
    /// <param name="report">The domain report to map.</param>
    /// <returns>The corresponding API response.</returns>
    public static InventoryReportResponse ToResponse(InventoryReport report)
    {
        var categories = report.Categories
            .Select(static c => new CategoryResponse(
                c.Category,
                c.TotalInventoryValue,
                c.AvailableRecordCount,
                new RecordReference(c.MostRecentlyUpdated.Id, c.MostRecentlyUpdated.Name, c.MostRecentlyUpdated.LastUpdatedUtc),
                [.. c.Items.Select(static i => new ItemResponse(i.Id, i.Name, i.Price, i.Quantity, i.LastUpdatedUtc))]))
            .ToList();

        var s = report.Statistics;
        var summary = new ReportSummaryResponse(
            report.GeneratedAt.UtcDateTime,
            s.RecordsRetrieved,
            s.DuplicatesDiscarded,
            s.InvalidRecordsExcluded,
            s.ZeroQuantityRecordsExcluded,
            s.AvailableRecords,
            s.Warnings);

        return new InventoryReportResponse(categories, summary);
    }
}
