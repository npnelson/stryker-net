using SchedulerSaturation.TargetProject;

namespace SchedulerSaturation.CommonTests;

/// <summary>
/// Defines the shared semantic assertion used by both framework adapters.
/// </summary>
public static class SaturationOracle
{
    /// <summary>
    /// Gets the deterministic input to the saturation workload.
    /// </summary>
    public const int Seed = 17;

    /// <summary>
    /// Gets the checksum expected after all committed assignment sites execute.
    /// </summary>
    public const int ExpectedChecksum = 51_377;

    /// <summary>
    /// Verifies the shared target result without framework-specific assertion behavior.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The target does not produce the declared deterministic checksum.
    /// </exception>
    public static void Verify()
    {
        var actual = SaturationWorkload.CalculateChecksum(Seed);

        if (actual != ExpectedChecksum)
        {
            throw new InvalidOperationException(
                $"Expected checksum {ExpectedChecksum}, but observed {actual}.");
        }
    }
}
