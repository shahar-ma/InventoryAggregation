namespace InventoryAggregation.Api.Inventory;

public enum InventoryDataFailure
{
    /// <summary>Unreachable, 5xx/408/429 after retries, or circuit open.</summary>
    Unavailable,

    /// <summary>The upstream did not answer within the configured time limits.</summary>
    Timeout,

    /// <summary>The upstream answered, but with something we cannot trust (4xx, malformed or inconsistent data).</summary>
    InvalidResponse,
}

/// <summary>
/// The single exception type the data-access layer surfaces. Messages are safe to log; the HTTP endpoint never copies
/// them into a response.
/// </summary>
public sealed class InventoryDataException(InventoryDataFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public InventoryDataFailure Failure { get; } = failure;
}
