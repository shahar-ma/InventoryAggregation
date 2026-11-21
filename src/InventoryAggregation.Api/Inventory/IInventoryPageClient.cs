using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Inventory;

/// <summary>One page of the upstream response with a complete pagination envelope.</summary>
public sealed record InventoryPage(
    int Page,
    int PageSize,
    int TotalPages,
    int TotalRecords,
    IReadOnlyList<RawInventoryRecord?> Records);

/// <summary>Talks HTTP to the Inventory API, one page per call. Throws <see cref="InventoryDataException"/> on any failure.</summary>
public interface IInventoryPageClient
{
    Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
