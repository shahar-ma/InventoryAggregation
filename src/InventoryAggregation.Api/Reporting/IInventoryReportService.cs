using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Reporting;

public interface IInventoryReportService
{
    /// <exception cref="Inventory.InventoryDataException">The dataset could not be retrieved completely and consistently.</exception>
    Task<InventoryReport> GetReportAsync(CancellationToken cancellationToken);
}
