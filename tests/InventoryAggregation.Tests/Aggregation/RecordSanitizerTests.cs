using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Tests.Support;

namespace InventoryAggregation.Tests.Aggregation;

public class RecordSanitizerTests
{
    private static RawInventoryRecord Valid() => TestData.Raw(7, "Hardware", 2.50m, 3, name: "Widget");

    [Fact]
    public void Sanitize_ConvertsValidRecords_TrimsText_AndNormalisesTimestampToUtc()
    {
        var local = new DateTimeOffset(2026, 10, 15, 14, 30, 0, TimeSpan.FromHours(2));
        var raw = new RawInventoryRecord(7, "  Widget  ", "  Hardware ", 2.50m, 3, local);

        var result = RecordSanitizer.Sanitize([raw]);

        var record = Assert.Single(result.Records);
        Assert.Empty(result.Problems);
        Assert.Equal(new InventoryRecord(7, "Widget", "Hardware", 2.50m, 3, new DateTime(2026, 10, 15, 12, 30, 0, DateTimeKind.Utc)), record);
        Assert.Equal(DateTimeKind.Utc, record.LastUpdatedUtc.Kind);
    }

    [Fact]
    public void Sanitize_KeepsZeroQuantityRecords_BecauseExcludingThemIsTheAggregatorsJob()
    {
        var result = RecordSanitizer.Sanitize([Valid() with { Quantity = 0 }]);

        Assert.Single(result.Records);
        Assert.Empty(result.Problems);
    }

    [Theory]
    [InlineData("null", "the record is null", "unknown id")]
    [InlineData("missing-id", "id is missing", "unknown id")]
    [InlineData("missing-name", "name is missing or blank", "id 7")]
    [InlineData("blank-name", "name is missing or blank", "id 7")]
    [InlineData("blank-category", "category is missing or blank", "id 7")]
    [InlineData("missing-price", "price is missing", "id 7")]
    [InlineData("negative-price", "price is negative", "id 7")]
    [InlineData("missing-quantity", "quantity is missing", "id 7")]
    [InlineData("negative-quantity", "quantity is negative", "id 7")]
    [InlineData("missing-timestamp", "lastUpdated is missing", "id 7")]
    public void Sanitize_ExcludesUntrustworthyRecords_AndExplainsWhy(string scenario, string expectedProblem, string expectedLabel)
    {
        RawInventoryRecord? candidate = scenario switch
        {
            "null" => null,
            "missing-id" => Valid() with { Id = null },
            "missing-name" => Valid() with { Name = null },
            "blank-name" => Valid() with { Name = "   " },
            "blank-category" => Valid() with { Category = " " },
            "missing-price" => Valid() with { Price = null },
            "negative-price" => Valid() with { Price = -0.01m },
            "missing-quantity" => Valid() with { Quantity = null },
            "negative-quantity" => Valid() with { Quantity = -1 },
            "missing-timestamp" => Valid() with { LastUpdated = null },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };

        var result = RecordSanitizer.Sanitize([candidate]);

        Assert.Empty(result.Records);
        var problem = Assert.Single(result.Problems);
        Assert.Contains(expectedProblem, problem);
        Assert.Contains(expectedLabel, problem);
        Assert.Contains("position 0", problem);
    }

    [Fact]
    public void Sanitize_ReportsThePositionOfEachExcludedRecord_AndKeepsTheRest()
    {
        var result = RecordSanitizer.Sanitize([TestData.Raw(1), Valid() with { Price = -1m }, TestData.Raw(3)]);

        Assert.Equal(new[] { 1L, 3L }, result.Records.Select(r => r.Id));
        Assert.Contains("position 1", Assert.Single(result.Problems));
    }

    [Fact]
    public void Sanitize_RejectsNullInput() =>
        Assert.Throws<ArgumentNullException>(() => RecordSanitizer.Sanitize(null!));
}
