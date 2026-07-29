using SchedulerSaturation.CommonTests;
using TUnit.Core;

namespace SchedulerSaturation.Tests.TUnit;

public sealed class SaturationTests
{
    /// <summary>
    /// Executes the single shared scheduler-saturation workload.
    /// </summary>
    [Test]
    public void WorkloadProducesExpectedChecksum()
    {
        SaturationOracle.Verify();
    }
}
