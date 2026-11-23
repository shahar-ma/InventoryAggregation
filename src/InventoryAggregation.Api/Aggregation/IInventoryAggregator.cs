using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Aggregation;

public interface IInventoryAggregator
{
    /// <summary>Groups available records by category and calculates each category's totals and latest record.</summary>
    /// <param name="records">The validated inventory records to aggregate.</param>
    /// <returns>The category aggregates and the number of zero-quantity records excluded.</returns>
    AggregationResult Aggregate(IEnumerable<InventoryRecord> records);
}
