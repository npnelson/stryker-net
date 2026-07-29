using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace Stryker.Benchmarks;

[MemoryDiagnoser]
public class RegisterCoverageSequentialBenchmarks
{
    private int[] _hits = null!;

    [Params(100, 1_000, 10_000)]
    public int DistinctMutants { get; set; }

    [Params(1, 10)]
    public int Passes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _hits = CoverageBenchmarkEnvironment.BuildRepeatedAscendingHits(DistinctMutants, Passes);
        CoverageBenchmarkEnvironment.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => CoverageBenchmarkEnvironment.Stop();

    [Benchmark(OperationsPerInvoke = 1)]
    public IList<int>[] CaptureCoverageGeneration()
    {
        foreach (var id in _hits)
        {
            MutantControl.IsActive(id);
        }

        return CoverageBenchmarkEnvironment.FinishGeneration();
    }
}

[MemoryDiagnoser]
public class RegisterCoverageDuplicateBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int HitCount { get; set; }

    [GlobalSetup]
    public void Setup() => CoverageBenchmarkEnvironment.Start();

    [GlobalCleanup]
    public void Cleanup() => CoverageBenchmarkEnvironment.Stop();

    [Benchmark(OperationsPerInvoke = 1)]
    public IList<int>[] CaptureRepeatedMutant()
    {
        for (var i = 0; i < HitCount; i++)
        {
            MutantControl.IsActive(42);
        }

        return CoverageBenchmarkEnvironment.FinishGeneration();
    }
}

[MemoryDiagnoser]
public class RegisterCoverageStaticPromotionBenchmarks
{
    [Params(100, 1_000, 10_000)]
    public int DistinctMutants { get; set; }

    [GlobalSetup]
    public void Setup() => CoverageBenchmarkEnvironment.Start();

    [GlobalCleanup]
    public void Cleanup() => CoverageBenchmarkEnvironment.Stop();

    [Benchmark(OperationsPerInvoke = 1)]
    public IList<int>[] CaptureNormalThenStaticCoverage()
    {
        for (var id = 0; id < DistinctMutants; id++)
        {
            MutantControl.IsActive(id);
        }

        using (new MutantContext())
        {
            for (var id = 0; id < DistinctMutants; id++)
            {
                MutantControl.IsActive(id);
            }
        }

        return CoverageBenchmarkEnvironment.FinishGeneration();
    }
}

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class RegisterCoverageConcurrentBenchmarks
{
    private int[] _hits = null!;
    private ParallelOptions _parallelOptions = null!;

    [Params(1_000, 10_000)]
    public int DistinctMutants { get; set; }

    [Params(1, 4, 8)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _hits = CoverageBenchmarkEnvironment.BuildRepeatedAscendingHits(DistinctMutants, 10);
        _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = WorkerCount };
        CoverageBenchmarkEnvironment.Start();
    }

    [GlobalCleanup]
    public void Cleanup() => CoverageBenchmarkEnvironment.Stop();

    [Benchmark(OperationsPerInvoke = 1)]
    public IList<int>[] CaptureCoverageConcurrently()
    {
        Parallel.For(
            0,
            _hits.Length,
            _parallelOptions,
            index => MutantControl.IsActive(_hits[index]));

        return CoverageBenchmarkEnvironment.FinishGeneration();
    }
}
