using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace InventoryAggregation.Tests.Support;

internal static class TestData
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 15, 12, 30, 0, TimeSpan.Zero);

    public static RawInventoryRecord Raw(
        long id,
        string category = "Electronics",
        decimal price = 10m,
        int quantity = 1,
        DateTimeOffset? updated = null,
        string? name = null) =>
        new(id, name ?? $"Item {id}", category, price, quantity, updated ?? T0);

    public static RawInventoryRecord[] Records(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Raw(i))];

    public static InventoryRecord Record(
        long id,
        string category = "Electronics",
        decimal price = 10m,
        int quantity = 1,
        DateTime? updatedUtc = null,
        string? name = null) =>
        new(id, name ?? $"Item {id}", category, price, quantity, updatedUtc ?? T0.UtcDateTime);

    public static InventoryPage Page(int page, int pageSize, int totalPages, int totalRecords, params RawInventoryRecord?[] records) =>
        new(page, pageSize, totalPages, totalRecords, records, NullLogger<InventoryPage>.Instance);

    /// <summary>A fully consistent view of a dataset of <paramref name="total"/> records (ids 1..total) at the given page.</summary>
    public static InventoryPage DatasetPage(int total, int pageSize, int page)
    {
        var totalPages = (total + pageSize - 1) / pageSize;
        var start = (page - 1) * pageSize + 1;
        var count = Math.Max(0, Math.Min(pageSize, total - (page - 1) * pageSize));
        var records = Enumerable.Range(start, count).Select(i => Raw(i)).ToArray();
        return Page(page, pageSize, totalPages, total, records);
    }

    // ---- JSON helpers (wire format) ----

    public static object RecordJson(
        long id,
        string category = "Electronics",
        decimal price = 149.99m,
        int quantity = 12,
        string updated = "2026-10-15T12:30:00Z") =>
        new { id, name = $"Item {id}", category, price, quantity, lastUpdated = updated };

    public static string PageJson(int page, int pageSize, int totalPages, int totalRecords, params object[] records) =>
        JsonSerializer.Serialize(new { records, page, pageSize, totalPages, totalRecords });

    public static string SinglePageJson => PageJson(1, 25, 1, 1, RecordJson(1001));
}
