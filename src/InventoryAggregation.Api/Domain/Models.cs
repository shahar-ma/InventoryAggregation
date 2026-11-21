namespace InventoryAggregation.Api.Domain;

/// <summary>
/// A record exactly as the upstream sent it. Every field is nullable so that a missing field is detected and reported
/// by <see cref="Aggregation.RecordSanitizer"/> instead of silently becoming a default value.
/// </summary>
public sealed record RawInventoryRecord(
    long? Id,
    string? Name,
    string? Category,
    decimal? Price,
    int? Quantity,
    DateTimeOffset? LastUpdated);

/// <summary>A validated record. <see cref="LastUpdatedUtc"/> always has <see cref="DateTimeKind.Utc"/>.</summary>
public sealed record InventoryRecord(
    long Id,
    string Name,
    string Category,
    decimal Price,
    int Quantity,
    DateTime LastUpdatedUtc);

public sealed record CategoryAggregate(
    string Category,
    decimal TotalInventoryValue,
    int AvailableRecordCount,
    InventoryRecord MostRecentlyUpdated,
    IReadOnlyList<InventoryRecord> Items);

public sealed record AggregationResult(IReadOnlyList<CategoryAggregate> Categories, int ZeroQuantityExcluded);

public sealed record ReportStatistics(
    int RecordsRetrieved,
    int DuplicatesDiscarded,
    int InvalidRecordsExcluded,
    int ZeroQuantityRecordsExcluded,
    int AvailableRecords,
    IReadOnlyList<string> Warnings);

public sealed record InventoryReport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CategoryAggregate> Categories,
    ReportStatistics Statistics);
