using InventoryAggregation.Api.Configuration;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace InventoryAggregation.Api.Inventory;

/// <summary>
/// Walks every page of the upstream API and proves the result is complete and internally consistent before handing it on.
/// Knows about pagination, not about HTTP (that is <see cref="IInventoryPageClient"/>) and not about aggregation.
/// </summary>
/// <remarks>
/// Checks, per page: the echoed page number, the page size, totalPages versus totalRecords, that the totals do not drift
/// between pages, and that the page holds exactly the number of records its position implies. Because the expected counts of
/// all pages sum to totalRecords, a dataset that passes every page check is complete. A page that fails is re-fetched
/// a bounded number of times (a short or empty 200 is often transient) and then fails the whole retrieval.
/// </remarks>
public sealed class PagedInventoryRecordSource(
    IInventoryPageClient client,
    IOptions<InventoryApiOptions> options,
    ILogger<PagedInventoryRecordSource> logger) : IInventoryRecordSource
{
    internal sealed record PageExpectation(int PageSize, int TotalPages, int TotalRecords);

    /// <summary>Fetches every page and returns the de-duplicated dataset.</summary>
    /// <param name="cancellationToken">Cancels page retrieval.</param>
    /// <returns>The complete raw dataset and the number of identical duplicates discarded.</returns>
    /// <exception cref="InventoryDataException">The upstream response is incomplete, inconsistent, or exceeds configured limits.</exception>
    public async Task<RetrievedInventory> GetAllAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var first = await FetchPageAsync(1, settings.PageSize, expected: null, settings.MaxPageAttempts, cancellationToken);

        if (first.TotalPages > settings.MaxPages)
        {
            throw new InventoryDataException(
                InventoryDataFailure.InvalidResponse,
                $"The inventory API reports {first.TotalPages} pages, which exceeds the configured safety limit of {settings.MaxPages}.");
        }

        // The server's echoed page size is authoritative: if it clamps our request, later pages use its size.
        var expectation = new PageExpectation(first.PageSize, first.TotalPages, first.TotalRecords);
        var pages = new InventoryPage[Math.Max(1, first.TotalPages)];
        pages[0] = first;

        await Parallel.ForEachAsync(
            Enumerable.Range(2, Math.Max(0, first.TotalPages - 1)),
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentRequests, CancellationToken = cancellationToken },
            async (page, token) =>
            {
                pages[page - 1] = await FetchPageAsync(page, expectation.PageSize, expectation, settings.MaxPageAttempts, token);
            });

        var retrieved = Deduplicate([.. pages.SelectMany(static p => p.Records)]);
        logger.LogRetrievedRecords(retrieved.Records.Count, pages.Length, retrieved.DuplicatesDiscarded);
        return retrieved;
    }

    /// <summary>
    /// Ids are documented as unique. An identical repeat is discarded and counted; two different records with the same id
    /// mean we cannot know which is right, so retrieval fails rather than guessing.
    /// </summary>
    private static RetrievedInventory Deduplicate(List<RawInventoryRecord?> records)
    {
        var firstById = new Dictionary<long, RawInventoryRecord>();
        var unique = new List<RawInventoryRecord?>(records.Count);
        var duplicates = 0;

        foreach (var record in records)
        {
            if (record is null || record.Id is null)
            {
                // Cannot be de-duplicated; the sanitizer will report it.
                unique.Add(record);
                continue;
            }

            var id = record.Id.Value;

            if (firstById.TryAdd(id, record))
            {
                unique.Add(record);
                continue;
            }

            if (firstById[id] != record)
            {
                throw new InventoryDataException(
                    InventoryDataFailure.InvalidResponse,
                    $"The inventory API returned conflicting records for id {id}.");
            }

            duplicates++;
        }

        return new RetrievedInventory(unique, duplicates);
    }

    private async Task<InventoryPage> FetchPageAsync(
            int page,
        int requestedPageSize,
        PageExpectation? expected,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        string lastProblem = "Unknown";
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await client.GetPageAsync(page, requestedPageSize, cancellationToken);
            var problem = result.Validate(page, requestedPageSize, expected);

            if (problem is null)
                return result;

            lastProblem = problem;
            logger.LogPageFailedValidation(page, attempt, maxAttempts, lastProblem);
        }
        throw new InventoryDataException(
            InventoryDataFailure.InvalidResponse,
            $"Page {page} failed validation after {maxAttempts} attempt(s): {lastProblem}.");
    }
}
