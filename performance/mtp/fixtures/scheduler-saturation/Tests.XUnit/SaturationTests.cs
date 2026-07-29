using SchedulerSaturation.CommonTests;
using Xunit;

namespace SchedulerSaturation.Tests.XUnit;

public sealed class SaturationTests
{
    /// <summary>
    /// Executes the single shared scheduler-saturation workload.
    /// </summary>
    [Fact]
    public void WorkloadProducesExpectedChecksum()
    {
        SaturationOracle.Verify();
    }
}
