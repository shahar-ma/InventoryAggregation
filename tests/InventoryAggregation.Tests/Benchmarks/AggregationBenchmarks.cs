using BenchmarkDotNet.Attributes;
using InventoryAggregation.Api.Aggregation;
using InventoryAggregation.Api.Domain;

namespace InventoryAggregation.Tests.Benchmarks;

/// <summary>Cost of the pure transformation (no I/O) as the dataset grows: sanitize, aggregate, and both together.</summary>
[MemoryDiagnoser]
public class AggregationBenchmarks
{
    private static readonly string[] Categories = ["Electronics", "Furniture", "Grocery", "Hardware", "Office", "Toys"];

    private readonly InventoryAggregator _aggregator = new();
    private RawInventoryRecord[] _raw = [];
    private InventoryRecord[] _valid = [];

    [Params(1_000, 100_000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        _raw = Enumerable.Range(1, RecordCount)
            .Select(i => new RawInventoryRecord(
                i,
                $"Item {i}",
                Categories[random.Next(Categories.Length)],
                Math.Round((decimal)random.NextDouble() * 500m + 1m, 2),
                random.Next(0, 50),
                epoch.AddMinutes(random.Next(0, 500_000))))
            .ToArray();

        _valid = RecordSanitizer.Sanitize(_raw).Records.ToArray();
    }

    [Benchmark]
    public int Sanitize() => RecordSanitizer.Sanitize(_raw).Records.Count;

    [Benchmark]
    public int Aggregate() => _aggregator.Aggregate(_valid).Categories.Count;

    [Benchmark]
    public int SanitizeAndAggregate() => _aggregator.Aggregate(RecordSanitizer.Sanitize(_raw).Records).Categories.Count;
}
