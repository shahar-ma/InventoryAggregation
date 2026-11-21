using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Inventory;

/// <summary>Wire format of <c>GET /api/records</c>. Envelope fields are nullable so a missing one is detectable.</summary>
internal sealed record InventoryPageDto(
    List<RawInventoryRecord?>? Records,
    int? Page,
    int? PageSize,
    int? TotalPages,
    int? TotalRecords);
