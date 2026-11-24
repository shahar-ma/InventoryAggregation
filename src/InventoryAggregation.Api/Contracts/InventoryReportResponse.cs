namespace InventoryAggregation.Api.Contracts;

/// <summary>
/// Public response shape. Matches the shape suggested in the assignment, plus a <c>summary</c> block that makes data-quality
/// decisions (exclusions, discarded duplicates) visible to the caller instead of silent.
/// </summary>
/// <summary>Aggregated inventory categories and report-wide data-quality statistics.</summary>
/// <param name="Categories">Category summaries containing totals and available items.</param>
/// <param name="Summary">Report generation time and counts of retrieved, excluded, and available records.</param>
public sealed record InventoryReportResponse(IReadOnlyList<CategoryResponse> Categories, ReportSummaryResponse Summary);

/// <summary>Available inventory totals and records for one category.</summary>
/// <param name="Category">The category name.</param>
/// <param name="TotalInventoryValue">The sum of price multiplied by quantity for available records, rounded to two decimal places.</param>
/// <param name="AvailableRecordCount">The number of available records in this category.</param>
/// <param name="MostRecentlyUpdated">The most recently updated available record in the category.</param>
/// <param name="Items">The available records in this category, ordered by record ID.</param>
public sealed record CategoryResponse(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    RecordReference MostRecentlyUpdated,
    IReadOnlyList<ItemResponse> Items);

/// <summary>Reference to a record, used for the category's most recently updated item.</summary>
/// <param name="Id">The upstream record ID.</param>
/// <param name="Name">The record name.</param>
/// <param name="LastUpdated">The last-updated timestamp in UTC.</param>
public sealed record RecordReference(long Id, string Name, DateTime LastUpdated);

/// <summary>An available inventory record contributing to a category total.</summary>
/// <param name="Id">The upstream record ID.</param>
/// <param name="Name">The record name.</param>
/// <param name="Price">The unit price.</param>
/// <param name="Quantity">The available quantity, always greater than zero.</param>
/// <param name="LastUpdated">The last-updated timestamp in UTC.</param>
public sealed record ItemResponse(long Id, string Name, decimal Price, int Quantity, DateTime LastUpdated);

/// <summary>Counts and warnings describing the inventory report generation.</summary>
/// <param name="GeneratedAt">The report generation timestamp in UTC.</param>
/// <param name="RecordsRetrieved">The number of raw records retrieved before duplicate removal.</param>
/// <param name="DuplicatesDiscarded">The number of identical duplicate records discarded.</param>
/// <param name="InvalidRecordsExcluded">The number of invalid records excluded from aggregation.</param>
/// <param name="ZeroQuantityRecordsExcluded">The number of zero-quantity records excluded from aggregation.</param>
/// <param name="AvailableRecords">The number of records included in category results.</param>
/// <param name="Warnings">Descriptions of invalid records excluded from aggregation, capped at 50 entries.</param>
public sealed record ReportSummaryResponse(
    DateTime GeneratedAt,
    int RecordsRetrieved,
    int DuplicatesDiscarded,
    int InvalidRecordsExcluded,
    int ZeroQuantityRecordsExcluded,
    int AvailableRecords,
    IReadOnlyList<string> Warnings);
