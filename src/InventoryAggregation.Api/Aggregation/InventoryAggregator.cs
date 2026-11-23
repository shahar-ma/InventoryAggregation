using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Aggregation;

/// <summary>
/// The required aggregation: drop zero-quantity records, group by category, then total value, count, most recently
/// updated record and the contributing items per category. Knows nothing about HTTP; deterministic output ordering.
/// </summary>
public sealed class InventoryAggregator : IInventoryAggregator
{
    /// <summary>Groups available records by category and calculates each category's totals and latest record.</summary>
    /// <param name="records">The validated inventory records to aggregate.</param>
    /// <returns>The category aggregates and the number of zero-quantity records excluded.</returns>
    public AggregationResult Aggregate(IEnumerable<InventoryRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var all = records.ToList();
        var available = all.Where(static r => r.Quantity > 0).ToList();

        var categories = available
            .GroupBy(static r => r.Category, StringComparer.OrdinalIgnoreCase)
            .Select(BuildCategory)
            .OrderBy(static c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AggregationResult(categories, all.Count - available.Count);
    }

    private static CategoryAggregate BuildCategory(IGrouping<string, InventoryRecord> group)
    {
        var items = group.OrderBy(static r => r.Id).ToList();

        // Ties on lastUpdated are broken by the lowest id so the result is deterministic.
        var mostRecent = items
            .OrderByDescending(static r => r.LastUpdatedUtc)
            .ThenBy(static r => r.Id)
            .First();

        var total = Math.Round(items.Sum(static r => r.Price * r.Quantity), 2, MidpointRounding.AwayFromZero);

        // The display name is the spelling used by the lowest-id record in the group.
        return new CategoryAggregate(items[0].Category, total, items.Count, mostRecent, items);
    }
}
