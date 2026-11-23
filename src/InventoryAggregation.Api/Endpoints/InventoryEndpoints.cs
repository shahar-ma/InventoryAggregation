using InventoryAggregation.Api.Contracts;
using InventoryAggregation.Api.Inventory;
using InventoryAggregation.Api.Reporting;
using Microsoft.AspNetCore.Http.HttpResults;

namespace InventoryAggregation.Api.Endpoints;

public static class InventoryEndpoints
{
    public const string CategoriesRoute = "/api/inventory/categories";

    /// <summary>Maps the inventory category report endpoint.</summary>
    /// <param name="routes">The endpoint route builder to add the route to.</param>
    /// <returns>The route builder, for further endpoint configuration.</returns>
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(CategoriesRoute, GetCategoriesAsync)
            .WithName("GetInventoryCategories")
            .WithSummary("Inventory aggregated by category (zero-quantity records excluded).")
            .Produces<InventoryReportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return routes;
    }

    private static async Task<IResult> GetCategoriesAsync(IInventoryReportService reports, CancellationToken cancellationToken)
    {
        try
        {
            var report = await reports.GetReportAsync(cancellationToken);
            return TypedResults.Ok(InventoryReportMapper.ToResponse(report));
        }
        catch (InventoryDataException ex)
        {
            return ToProblem(ex);
        }
    }

    // Deliberately generic: upstream details (hostnames, status codes, ids) stay in the logs, not in the response.
    private static ProblemHttpResult ToProblem(InventoryDataException ex)
    {
        var (status, title) = ex.Failure switch
        {
            InventoryDataFailure.Timeout => (StatusCodes.Status504GatewayTimeout, "The inventory service timed out."),
            InventoryDataFailure.Unavailable => (StatusCodes.Status503ServiceUnavailable, "The inventory service is unavailable."),
            _ => (StatusCodes.Status502BadGateway, "The inventory service returned data that could not be used."),
        };

        return TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: "A complete and consistent inventory dataset could not be retrieved. Please try again later.");
    }
}
