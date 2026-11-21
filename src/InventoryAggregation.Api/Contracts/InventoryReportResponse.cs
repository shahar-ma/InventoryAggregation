namespace InventoryAggregation.Api.Contracts;

/// <summary>
/// Public response shape. Matches the shape suggested in the assignment, plus a <c>summary</c> block that makes data-quality
/// decisions (exclusions, discarded duplicates) visible to the caller instead of silent.
/// </summary>
public sealed record InventoryReportResponse(IReadOnlyList<CategoryResponse> Categories, ReportSummaryResponse Summary);

public sealed record CategoryResponse(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    RecordReference MostRecentlyUpdated,
    IReadOnlyList<ItemResponse> Items);

public sealed record RecordReference(long Id, string Name, DateTime LastUpdated);

public sealed record ItemResponse(long Id, string Name, decimal Price, int Quantity, DateTime LastUpdated);

public sealed record ReportSummaryResponse(
    DateTime GeneratedAt,
    int RecordsRetrieved,
    int DuplicatesDiscarded,
    int InvalidRecordsExcluded,
    int ZeroQuantityRecordsExcluded,
    int AvailableRecords,
    IReadOnlyList<string> Warnings);
