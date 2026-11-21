using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Tests.Support;

namespace InventoryAggregation.Tests.Aggregation;

public class InventoryAggregatorTests
{
    private readonly InventoryAggregator _sut = new();

    [Fact]
    public void Aggregate_ReproducesTheAssignmentExample()
    {
        var printer = TestData.Record(1001, "Electronics", 149.99m, 12) with { Name = "Thermal Receipt Printer" };

        var result = _sut.Aggregate([printer]);

        var category = Assert.Single(result.Categories);
        Assert.Equal("Electronics", category.Category);
        Assert.Equal(1799.88m, category.TotalInventoryValue);
        Assert.Equal(1, category.AvailableRecordCount);
        Assert.Equal(printer, category.MostRecentlyUpdated);
        Assert.Equal(new[] { printer }, category.Items);
        Assert.Equal(0, result.ZeroQuantityExcluded);
    }

    [Fact]
    public void Aggregate_ExcludesZeroQuantityRecords_FromEveryFigure()
    {
        var kept = TestData.Record(1, "Electronics", 10m, 2, T(0));
        var zeroButNewer = TestData.Record(2, "Electronics", 1000m, 0, T(5));
        var onlyZero = TestData.Record(3, "Ghost", 5m, 0);

        var result = _sut.Aggregate([kept, zeroButNewer, onlyZero]);

        var category = Assert.Single(result.Categories);   // "Ghost" disappears entirely
        Assert.Equal(20m, category.TotalInventoryValue);
        Assert.Equal(1, category.AvailableRecordCount);
        Assert.Equal(kept, category.MostRecentlyUpdated);  // the newer zero-quantity record must not win
        Assert.Equal(new[] { kept }, category.Items);
        Assert.Equal(2, result.ZeroQuantityExcluded);
    }

    [Fact]
    public void Aggregate_GroupsCategoriesCaseInsensitively_AndOrdersOutputDeterministically()
    {
        var furniture = TestData.Record(5, "Furniture", 100m, 1);
        var electronics = TestData.Record(2, "Electronics", 10m, 1);
        var electronicsLower = TestData.Record(3, "electronics", 20m, 1);

        var result = _sut.Aggregate([furniture, electronicsLower, electronics]);

        Assert.Equal(new[] { "Electronics", "Furniture" }, result.Categories.Select(c => c.Category));
        var merged = result.Categories[0];
        Assert.Equal(2, merged.AvailableRecordCount);
        Assert.Equal(30m, merged.TotalInventoryValue);
        Assert.Equal(new[] { 2L, 3L }, merged.Items.Select(i => i.Id));  // items ordered by id
    }

    [Fact]
    public void Aggregate_PicksTheNewestRecord_AndBreaksTiesByLowestId()
    {
        var older = TestData.Record(1, "A", 1m, 1, T(0));
        var newestHighId = TestData.Record(9, "A", 1m, 1, T(2));
        var tieLowId = TestData.Record(4, "A", 1m, 1, T(2));

        var result = _sut.Aggregate([older, newestHighId, tieLowId]);

        Assert.Equal(4, Assert.Single(result.Categories).MostRecentlyUpdated.Id);
    }

    [Fact]
    public void Aggregate_SumsPriceTimesQuantity_AndRoundsHalfAwayFromZeroToCents()
    {
        var sum = _sut.Aggregate([TestData.Record(1, "A", 19.99m, 3), TestData.Record(2, "A", 5.50m, 2)]);
        var rounding = _sut.Aggregate([TestData.Record(3, "B", 1.005m, 1)]);

        Assert.Equal(70.97m, sum.Categories[0].TotalInventoryValue);
        Assert.Equal(1.01m, rounding.Categories[0].TotalInventoryValue);
    }

    [Fact]
    public void Aggregate_OfNothing_IsEmpty()
    {
        var result = _sut.Aggregate([]);

        Assert.Empty(result.Categories);
        Assert.Equal(0, result.ZeroQuantityExcluded);
    }

    [Fact]
    public void Aggregate_RejectsNullInput() =>
        Assert.Throws<ArgumentNullException>(() => _sut.Aggregate(null!));

    private static DateTime T(int hours) => TestData.T0.UtcDateTime.AddHours(hours);
}
