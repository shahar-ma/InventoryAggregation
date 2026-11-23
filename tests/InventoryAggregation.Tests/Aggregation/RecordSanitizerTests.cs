using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Domain;
using InventoryAggregation.Tests.Support;
using System.Diagnostics.CodeAnalysis;

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
    [InlineData("null", "Record at position 0 (unknown id) excluded: the record is null.")]
    [InlineData("missing-id", "Record at position 0 (unknown id) excluded: id is missing.")]
    [InlineData("missing-name", "Record at position 0 (id 7) excluded: name is missing or blank.")]
    [InlineData("blank-name", "Record at position 0 (id 7) excluded: name is missing or blank.")]
    [InlineData("blank-category", "Record at position 0 (id 7) excluded: category is missing or blank.")]
    [InlineData("missing-price", "Record at position 0 (id 7) excluded: price is missing.")]
    [InlineData("negative-price", "Record at position 0 (id 7) excluded: price is negative.")]
    [InlineData("missing-quantity", "Record at position 0 (id 7) excluded: quantity is missing.")]
    [InlineData("negative-quantity", "Record at position 0 (id 7) excluded: quantity is negative.")]
    [InlineData("missing-timestamp", "Record at position 0 (id 7) excluded: lastUpdated is missing.")]
    public void Sanitize_ExcludesUntrustworthyRecords_AndExplainsWhy(string scenario, string expectedProblem)
    {
        var result = RecordSanitizer.Sanitize([UntrustworthyRecord(scenario)]);

        Assert.Empty(result.Records);
        Assert.Equal(expectedProblem, Assert.Single(result.Problems));
    }

    [ExcludeFromCodeCoverage]
    private static RawInventoryRecord? UntrustworthyRecord(string scenario) => scenario switch
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

    [Fact]
    public void Sanitize_ReportsThePositionOfEachExcludedRecord_AndKeepsTheRest()
    {
        var result = RecordSanitizer.Sanitize([TestData.Raw(1), Valid() with { Price = -1m }, TestData.Raw(3)]);

        Assert.Equal([1L, 3L], result.Records.Select(r => r.Id));
        Assert.Contains("position 1", Assert.Single(result.Problems));
    }

    [Fact]
    [ExcludeFromCodeCoverage]
    public void Sanitize_RejectsNullInput() =>
        Assert.Throws<ArgumentNullException>(() => RecordSanitizer.Sanitize(null!));
}
