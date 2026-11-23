namespace InventoryAggregation.Api;

/// <summary>
/// Anchor type used by <c>WebApplicationFactory&lt;T&gt;</c> in the integration tests. Using an explicit marker avoids
/// clashing with the compiler-generated <c>Program</c> class (and the entry point xunit.v3 generates in the test project).
/// </summary>
#pragma warning disable S2094
public sealed class ApiMarker;
#pragma warning restore S2094
