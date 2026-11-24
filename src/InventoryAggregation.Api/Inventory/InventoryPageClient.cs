using InventoryAggregation.Api.Infrastructure;
using Polly;
using Polly.Timeout;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace InventoryAggregation.Api.Inventory;

/// <summary>
/// Typed <see cref="HttpClient"/> (created through <c>IHttpClientFactory</c>) for the Inventory API. Retries, attempt and
/// total timeouts and the circuit breaker are applied by the resilience handler in the HTTP pipeline; this class turns
/// whatever comes out of that pipeline - a response or an exception - into a page or an <see cref="InventoryDataException"/>.
/// </summary>
public sealed class InventoryPageClient(HttpClient httpClient, ILogger<InventoryPageClient> logger) : IInventoryPageClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Retrieves and parses one page of inventory records from the upstream API.</summary>
    /// <param name="page">The one-based page number to retrieve.</param>
    /// <param name="pageSize">The requested number of records per page.</param>
    /// <param name="cancellationToken">Cancels the HTTP request.</param>
    /// <returns>The page and its pagination metadata.</returns>
    /// <exception cref="InventoryDataException">The request fails or the response cannot be read as a valid page.</exception>
    public async Task<InventoryPage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        // Relative (no leading slash) so a base address that has a path prefix keeps it.
        var relativeUri = string.Create(CultureInfo.InvariantCulture, $"api/records?page={page}&pageSize={pageSize}");

        try
        {
            using var response = await httpClient.GetAsync(relativeUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw FromStatusCode(response.StatusCode, page);
            }

            var dto = await response.Content.ReadFromJsonAsync<InventoryPageDto>(SerializerOptions, cancellationToken);

            return ToPage(dto, logger);
        }
        catch (TimeoutRejectedException ex)
        {
            logger.LogApiPageCallTimeout(page, ex);
            throw new InventoryDataException(InventoryDataFailure.Timeout, $"The inventory API did not answer in time (page {page}).", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            // Open circuit breaker or rate limiter rejection: the request never left the process.
            logger.LogApiPageCallRejected(page, ex);
            throw new InventoryDataException(InventoryDataFailure.Unavailable, $"The inventory API is temporarily unavailable (page {page}).", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogApiPageCallFailure(page, ex);
            throw new InventoryDataException(InventoryDataFailure.Unavailable, $"The inventory API could not be reached (page {page}).", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled, but not by the caller: an HttpClient/handler timeout.
            logger.LogApiPageCallCanceled(page, ex);
            throw new InventoryDataException(InventoryDataFailure.Timeout, $"The inventory API call timed out (page {page}).", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogApiPageCallReturnInvalidContent(page, ex);
            throw new InventoryDataException(InventoryDataFailure.InvalidResponse, $"The inventory API returned an unreadable body (page {page}).", ex);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    private static InventoryPage ToPage(InventoryPageDto? dto, ILogger logger)
    {
        if (dto is null || dto.Page is null || dto.PageSize is null || dto.TotalPages is null || dto.TotalRecords is null || dto.Records is null)
        {
            throw new InventoryDataException(InventoryDataFailure.InvalidResponse, "The inventory API response is missing part of its pagination envelope.");
        }

        return new InventoryPage(dto.Page.Value, dto.PageSize.Value, dto.TotalPages.Value, dto.TotalRecords.Value, dto.Records, logger);
    }

    private InventoryDataException FromStatusCode(HttpStatusCode status, int page)
    {
        var failure = IsTransient(status) ? InventoryDataFailure.Unavailable : InventoryDataFailure.InvalidResponse;
        logger.LogApiPageCallFailureWithStatus((int)status, page);
        return new InventoryDataException(failure, $"The inventory API returned HTTP {(int)status} (page {page}).");
    }
}
