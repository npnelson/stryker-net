using System.Collections.ObjectModel;

namespace Stryker.Performance.ExperimentScheduling;

/// <summary>
/// Validates that a schedule still matches its versioned generator contract.
/// </summary>
public static class ExperimentScheduleValidator
{
    /// <summary>
    /// Checks schedule metadata, sequence coordinates, treatment balance, and seeded order.
    /// </summary>
    /// <param name="schedule">The complete schedule to validate.</param>
    /// <returns>A deterministic list of validation errors.</returns>
    public static ScheduleValidationResult Validate(ExperimentSchedule schedule)
    {
        var errors = new List<string>();

        if (!string.Equals(
                schedule.GeneratorVersion,
                ExperimentScheduleGenerator.Version,
                StringComparison.Ordinal))
        {
            errors.Add($"Unsupported generator version '{schedule.GeneratorVersion}'.");
        }

        if (!ExperimentScheduleGenerator.IsSupportedBlockCount(schedule.BlockCount))
        {
            errors.Add("Block count must be an even number between 2 and 10,000.");
        }

        var expectedObservationCount = schedule.BlockCount > 0
            ? (long)schedule.BlockCount * 4
            : 0;
        if (schedule.Observations.Count != expectedObservationCount)
        {
            errors.Add(
                $"Observation count {schedule.Observations.Count} does not match block count {schedule.BlockCount}.");
        }

        ValidateCoordinates(schedule.Observations, errors);
        ValidatePatterns(schedule, errors);
        ValidateSeededOrder(schedule, errors);

        return new ScheduleValidationResult(errors);
    }

    private static void ValidateCoordinates(
        IReadOnlyList<ScheduledObservation> observations,
        List<string> errors)
    {
        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            var expectedSequence = index + 1;
            var expectedBlock = (index / 4) + 1;
            var expectedPosition = (index % 4) + 1;
            var expectedId = ExperimentScheduleGenerator.ObservationId(expectedBlock, expectedPosition);

            if (observation.Sequence != expectedSequence)
            {
                errors.Add(
                    $"Observation at index {index} has sequence {observation.Sequence}; expected {expectedSequence}.");
            }

            if (observation.Block != expectedBlock)
            {
                errors.Add(
                    $"Observation at index {index} has block {observation.Block}; expected {expectedBlock}.");
            }

            if (observation.Position != expectedPosition)
            {
                errors.Add(
                    $"Observation at index {index} has position {observation.Position}; expected {expectedPosition}.");
            }

            if (!string.Equals(observation.ObservationId, expectedId, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Observation at index {index} has identifier '{observation.ObservationId}'; expected '{expectedId}'.");
            }

            if (observation.Treatment is not ("A" or "B"))
            {
                errors.Add(
                    $"Observation at index {index} has treatment '{observation.Treatment}'; expected A or B.");
            }
        }
    }

    private static void ValidatePatterns(ExperimentSchedule schedule, List<string> errors)
    {
        if (schedule.Observations.Count != (long)schedule.BlockCount * 4 || schedule.BlockCount <= 0)
        {
            return;
        }

        var abbaCount = 0;
        var baabCount = 0;

        for (var blockIndex = 0; blockIndex < schedule.BlockCount; blockIndex++)
        {
            var pattern = string.Concat(schedule.Observations
                .Skip(blockIndex * 4)
                .Take(4)
                .Select(observation => observation.Treatment));

            switch (pattern)
            {
                case "ABBA":
                    abbaCount++;
                    break;
                case "BAAB":
                    baabCount++;
                    break;
                default:
                    errors.Add($"Block {blockIndex + 1} has unsupported pattern '{pattern}'.");
                    break;
            }
        }

        if (abbaCount != schedule.BlockCount / 2 || baabCount != schedule.BlockCount / 2)
        {
            errors.Add(
                $"Schedule has {abbaCount} ABBA and {baabCount} BAAB blocks; expected {schedule.BlockCount / 2} of each.");
        }
    }

    private static void ValidateSeededOrder(ExperimentSchedule schedule, List<string> errors)
    {
        if (!string.Equals(
                schedule.GeneratorVersion,
                ExperimentScheduleGenerator.Version,
                StringComparison.Ordinal)
            || !ExperimentScheduleGenerator.IsSupportedBlockCount(schedule.BlockCount)
            || schedule.Observations.Count != (long)schedule.BlockCount * 4)
        {
            return;
        }

        var expected = ExperimentScheduleGenerator.Generate(schedule.Seed, schedule.BlockCount);
        if (!schedule.Observations.SequenceEqual(expected.Observations))
        {
            errors.Add("Schedule order does not match the declared seed and generator version.");
        }
    }
}

/// <summary>
/// The outcome of deterministic schedule validation.
/// </summary>
public sealed class ScheduleValidationResult
{
    internal ScheduleValidationResult(IEnumerable<string> errors) =>
        Errors = new ReadOnlyCollection<string>([.. errors]);

    /// <summary>
    /// Gets a value indicating whether the schedule satisfies every contract rule.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets validation errors in deterministic rule and observation order.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }
}
