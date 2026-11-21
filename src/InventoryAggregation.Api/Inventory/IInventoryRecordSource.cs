using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Inventory;

/// <summary>The complete, de-duplicated dataset as retrieved from upstream (records are still unvalidated).</summary>
public sealed record RetrievedInventory(IReadOnlyList<RawInventoryRecord?> Records, int DuplicatesDiscarded);

/// <summary>Produces the whole dataset or throws <see cref="InventoryDataException"/>; never a silently partial one.</summary>
public interface IInventoryRecordSource
{
    Task<RetrievedInventory> GetAllAsync(CancellationToken cancellationToken);
}
