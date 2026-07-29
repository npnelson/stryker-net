using System.Collections.ObjectModel;

namespace Stryker.Performance.ExperimentScheduling;

/// <summary>
/// A complete, versioned execution order for one paired experiment session.
/// The supplied observations are snapshotted so callers cannot provide a lazy sequence.
/// </summary>
/// <param name="generatorVersion">The algorithm contract used to create the schedule.</param>
/// <param name="seed">The signed seed used by the generator.</param>
/// <param name="blockCount">The number of four-observation blocks.</param>
/// <param name="observations">The complete execution order.</param>
public sealed class ExperimentSchedule(
    string generatorVersion,
    long seed,
    int blockCount,
    IReadOnlyList<ScheduledObservation> observations)
{
    /// <summary>
    /// Gets the algorithm contract used to create this schedule.
    /// </summary>
    public string GeneratorVersion { get; } = generatorVersion;

    /// <summary>
    /// Gets the signed seed used by the generator.
    /// </summary>
    public long Seed { get; } = seed;

    /// <summary>
    /// Gets the number of four-observation blocks.
    /// </summary>
    public int BlockCount { get; } = blockCount;

    /// <summary>
    /// Gets the complete execution order.
    /// </summary>
    public IReadOnlyList<ScheduledObservation> Observations { get; } =
        new ReadOnlyCollection<ScheduledObservation>([.. observations]);
}

/// <summary>
/// One precommitted observation in an experiment schedule.
/// </summary>
/// <param name="ObservationId">The stable block-and-position identifier.</param>
/// <param name="Sequence">The one-based position in the complete schedule.</param>
/// <param name="Block">The one-based block number.</param>
/// <param name="Position">The one-based position within the block.</param>
/// <param name="Treatment">The generic treatment label, either <c>A</c> or <c>B</c>.</param>
public sealed record ScheduledObservation(
    string ObservationId,
    int Sequence,
    int Block,
    int Position,
    string Treatment);
