using BenchmarkDotNet.Attributes;

namespace Stryker.Performance.Mtp.Microbenchmarks;

/// <summary>
/// Verifies BenchmarkDotNet discovery, process isolation, and exporter wiring.
/// </summary>
[BenchmarkCategory("HarnessVerification")]
public class HarnessVerificationBenchmark
{
    private WorkloadContract _contract = null!;

    /// <summary>
    /// Creates and validates the deterministic fixture outside the measured operation.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _contract = MtpWorkloadProfiles.Create();
        WorkloadContractValidator.Validate(_contract);
    }

    /// <summary>
    /// Provides a discoverable operation for end-to-end harness verification only.
    /// </summary>
    /// <returns>The number of deterministic workload profiles.</returns>
    [Benchmark(Description = "Read deterministic workload profile count")]
    public int ReadProfileCount() => _contract.Profiles.Length;
}
