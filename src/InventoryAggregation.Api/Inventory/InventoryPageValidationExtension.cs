using static InventoryAggregation.Api.Inventory.PagedInventoryRecordSource;

namespace InventoryAggregation.Api.Inventory;

internal static class InventoryPageValidationExtension
{
    /// <summary>Checks the page envelope, pagination totals, and expected record count for consistency.</summary>
    /// <param name="p">The page response to validate.</param>
    /// <param name="page">The one-based page number requested.</param>
    /// <param name="requestedPageSize">The page size requested for the first page.</param>
    /// <param name="expected">The pagination values established by the first page, or <see langword="null"/> for that first page.</param>
    /// <returns>A description of the validation failure, or <see langword="null"/> if the page is consistent.</returns>
    internal static string? Validate(this InventoryPage p, int page, int requestedPageSize, PageExpectation? expected)
    {
        if (expected is null)
        {
            var requestProblem = p.ValidateRequest(requestedPageSize);
            if (requestProblem is not null)
                return requestProblem;
        }
        else
        {
            if (p.PageSize != expected.PageSize)
            {
                return $"page size changed from {expected.PageSize} to {p.PageSize}";
            }

            if (p.TotalPages != expected.TotalPages || p.TotalRecords != expected.TotalRecords)
            {
                return "the reported totals changed between pages";
            }
        }

        if (!p.IsValid(page, out string? message))
            return message;

        return null;
    }
}