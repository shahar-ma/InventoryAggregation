using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Aggregation;

public interface IInventoryAggregator
{
    AggregationResult Aggregate(IEnumerable<InventoryRecord> records);
}
