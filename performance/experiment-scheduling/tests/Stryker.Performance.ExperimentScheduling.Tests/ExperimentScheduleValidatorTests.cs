using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Stryker.Performance.ExperimentScheduling.Tests;

[TestClass]
public sealed class ExperimentScheduleValidatorTests
{
    [TestMethod]
    public void GeneratedScheduleIsAccepted()
    {
        var result = ExperimentScheduleValidator.Validate(CreateSchedule());

        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    public void CorruptedSequenceIsRejected()
    {
        var schedule = ReplaceObservation(CreateSchedule(), 0, observation => observation with { Sequence = 2 });

        AssertInvalid(schedule, "sequence");
    }

    [TestMethod]
    public void CorruptedOrderIsRejectedEvenWhenCoordinatesAndBalanceRemainValid()
    {
        var schedule = CreateSchedule();
        var treatments = schedule.Observations
            .Chunk(4)
            .Select(block => block.Select(observation => observation.Treatment).ToArray())
            .ToArray();
        (treatments[0], treatments[1]) = (treatments[1], treatments[0]);
        var reordered = schedule.Observations.Select((observation, index) => observation with
        {
            Treatment = treatments[index / 4][index % 4],
        }).ToArray();

        var reorderedSchedule = new ExperimentSchedule(
            schedule.GeneratorVersion,
            schedule.Seed,
            schedule.BlockCount,
            reordered);

        AssertInvalid(reorderedSchedule, "declared seed");
    }

    [TestMethod]
    public void CorruptedBlockIsRejected()
    {
        var schedule = ReplaceObservation(CreateSchedule(), 0, observation => observation with { Block = 2 });

        AssertInvalid(schedule, "block");
    }

    [TestMethod]
    public void CorruptedPositionIsRejected()
    {
        var schedule = ReplaceObservation(CreateSchedule(), 0, observation => observation with { Position = 2 });

        AssertInvalid(schedule, "position");
    }

    [TestMethod]
    public void CorruptedTreatmentIsRejected()
    {
        var schedule = ReplaceObservation(CreateSchedule(), 0, observation => observation with { Treatment = "C" });

        AssertInvalid(schedule, "treatment");
    }

    [TestMethod]
    public void CorruptedGeneratorVersionIsRejected()
    {
        var source = CreateSchedule();
        var schedule = new ExperimentSchedule(
            "unknown/v2",
            source.Seed,
            source.BlockCount,
            source.Observations);

        AssertInvalid(schedule, "generator version");
    }

    private static ExperimentSchedule CreateSchedule() => ExperimentScheduleGenerator.Generate(42, 8);

    private static ExperimentSchedule ReplaceObservation(
        ExperimentSchedule schedule,
        int index,
        Func<ScheduledObservation, ScheduledObservation> replacement)
    {
        var observations = schedule.Observations.ToArray();
        observations[index] = replacement(observations[index]);

        return new ExperimentSchedule(
            schedule.GeneratorVersion,
            schedule.Seed,
            schedule.BlockCount,
            observations);
    }

    private static void AssertInvalid(ExperimentSchedule schedule, string expectedMessageFragment)
    {
        var result = ExperimentScheduleValidator.Validate(schedule);

        Assert.IsFalse(result.IsValid);
        Assert.Contains(
            expectedMessageFragment,
            string.Join(Environment.NewLine, result.Errors),
            StringComparison.OrdinalIgnoreCase);
    }
}
