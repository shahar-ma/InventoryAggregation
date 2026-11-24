using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Infrastructure;
namespace InventoryAggregation.Api.Inventory;

/// <summary>
/// One page of the upstream response with a complete pagination envelope.
/// </summary>
/// <param name="Page">The current page number.</param>
/// <param name="PageSize">The maximum number of records per page.</param>
/// <param name="TotalPages">The total number of available pages.</param>
/// <param name="TotalRecords">The total number of inventory records across all pages.</param>
/// <param name="Records">The collection of inventory records for the current page. Individual records may be null.</param>
/// <param name="PageLogger">The logger used for logging operations related to the current page.</param>
public sealed record InventoryPage(
    int Page,
    int PageSize,
    int TotalPages,
    int TotalRecords,
    IReadOnlyList<RawInventoryRecord?> Records,
    ILogger PageLogger)
{
    /// <summary>Determines whether this response matches the requested page and reports nonnegative totals.</summary>
    /// <param name="page">The page number requested from the upstream API.</param>
    /// <param name="message">When invalid, receives a description of the validation failure; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the response page matches <paramref name="page"/> and its totals are nonnegative; otherwise, <see langword="false"/>.</returns>
    internal bool IsValid(int page, out string? message)
    {
        message = null;
        if (Page != page)
        {
            message = $"requested page {page} but the response is for page {Page}";
            return false;
        }

        if (TotalRecords < 0 || TotalPages < 0)
        {
            message = "the response reports a negative total";
            return false;
        }

        var expectedPages = (int)(((long)TotalRecords + PageSize - 1) / PageSize);
        if (TotalPages != expectedPages)
        {
            message = $"totalPages {TotalPages} does not match totalRecords {TotalRecords} at page size {PageSize}";
            return false;
        }

        if (!IsExpectedCount(page, out int expectedCount))
        {
            PageLogger.LogUnexpectedPageSize(expectedCount, Records.Count);
        }

        return true;
    }

    internal bool IsExpectedCount(int page, out int expectedCount)
    {
        expectedCount =(int)Math.Clamp(TotalRecords - ((long)(page - 1) * PageSize), 0L, PageSize);
        return Records.Count == expectedCount;
    }

    /// <summary>Checks that the response page size is valid for the original request.</summary>
    /// <param name="requestedPageSize">The page size requested from the upstream API.</param>
    /// <returns>A description of the validation failure, or <see langword="null"/> if valid.</returns>
    internal string? ValidateRequest(int requestedPageSize)
    {
        if (PageSize < 1 || PageSize > requestedPageSize)
            return (string?)$"the response reports page size {PageSize} for a request of {requestedPageSize}";
        return null;
    }
}

/// <summary>Talks HTTP to the Inventory API, one page per call. Throws <see cref="InventoryDataException"/> on any failure.</summary>
public interface IInventoryPageClient
{
    /// <summary>Retrieves one page of inventory records from the upstream API.</summary>
    /// <param name="page">The one-based page number to retrieve.</param>
    /// <param name="pageSize">The requested number of records per page.</param>
    /// <param name="cancellationToken">Cancels the HTTP request.</param>
    /// <returns>The page and its pagination metadata.</returns>
    /// <exception cref="InventoryDataException">The page could not be retrieved or its response could not be read.</exception>
    Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken);
}
