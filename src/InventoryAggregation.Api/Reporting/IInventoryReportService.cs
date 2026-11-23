using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Api.Reporting;

public interface IInventoryReportService
{
    /// <summary>Gets the cached inventory report or builds a fresh report from the upstream dataset.</summary>
    /// <param name="cancellationToken">Cancels report retrieval and construction.</param>
    /// <returns>The complete inventory report.</returns>
    /// <exception cref="Inventory.InventoryDataException">The dataset could not be retrieved completely and consistently.</exception>
    Task<InventoryReport> GetReportAsync(CancellationToken cancellationToken);
}
