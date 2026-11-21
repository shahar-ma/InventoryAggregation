namespace InventoryAggregation.Tests.Support;

/// <summary>The cancellation token xunit.v3 cancels when the test run is aborted (keeps xUnit1051 quiet and tests stoppable).</summary>
internal static class Ct
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
