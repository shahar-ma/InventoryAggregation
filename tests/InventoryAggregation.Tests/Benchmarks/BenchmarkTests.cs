using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace InventoryAggregation.Tests.Benchmarks;

public class BenchmarkTests
{
    /// <summary>Opt-in switch: the real measurement run is slow, so it only happens when explicitly requested.</summary>
    public static bool RunBenchmarks =>
        string.Equals(Environment.GetEnvironmentVariable("INVENTORY_RUN_BENCHMARKS"), "true", StringComparison.OrdinalIgnoreCase);

    // The two facts below always run: they keep the benchmark code compiling AND correct, so a benchmark can never silently
    // measure the wrong thing.

    [Fact]
    public void AggregationBenchmarks_ComputeCorrectResults()
    {
        var benchmarks = new AggregationBenchmarks { RecordCount = 2_000 };
        benchmarks.Setup();

        Assert.Equal(2_000, benchmarks.Sanitize());
        Assert.InRange(benchmarks.Aggregate(), 1, 6);
        Assert.Equal(benchmarks.Aggregate(), benchmarks.SanitizeAndAggregate());
    }

    [Fact]
    public async Task PagedFetchBenchmark_RetrievesTheWholeDataset()
    {
        var benchmarks = new PagedFetchBenchmarks { Concurrency = 4 };
        benchmarks.Setup();

        Assert.Equal(PagedFetchBenchmarks.TotalRecords, await benchmarks.FetchAllPages());
    }

    /// <summary>
    /// Runs the BenchmarkDotNet suite in-process. Opt in with INVENTORY_RUN_BENCHMARKS=true and use a Release build
    /// (numbers from a Debug build are meaningless). Results are printed to the console and written to BenchmarkDotNet.Artifacts/.
    /// </summary>
    [Fact(Skip = "Opt-in: set INVENTORY_RUN_BENCHMARKS=true and run a Release build (see README).", SkipUnless = nameof(RunBenchmarks))]
    public void RunBenchmarkSuite()
    {
        IConfig config = ManualConfig.CreateEmpty()
            .AddJob(Job.ShortRun.WithToolchain(InProcessNoEmitToolchain.Instance))
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        foreach (var benchmarkType in new[] { typeof(AggregationBenchmarks), typeof(PagedFetchBenchmarks) })
        {
            var summary = BenchmarkRunner.Run(benchmarkType, config);

            Assert.False(summary.HasCriticalValidationErrors);
            Assert.NotEmpty(summary.Reports);
        }
    }
}
