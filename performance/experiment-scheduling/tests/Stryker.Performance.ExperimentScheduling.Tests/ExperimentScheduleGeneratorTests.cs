using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Stryker.Performance.ExperimentScheduling.Tests;

[TestClass]
public sealed class ExperimentScheduleGeneratorTests
{
    [TestMethod]
    public void SameSeedProducesByteForByteEquivalentSchedule()
    {
        var first = ExperimentScheduleGenerator.Generate(42, 8);
        var repeated = ExperimentScheduleGenerator.Generate(42, 8);

        CollectionAssert.AreEqual(CanonicalBytes(first), CanonicalBytes(repeated));
    }

    [TestMethod]
    public void ScheduleSnapshotsObservationsIntoReadOnlyStorage()
    {
        var generated = ExperimentScheduleGenerator.Generate(42, 2);
        var supplied = generated.Observations.ToArray();
        var schedule = new ExperimentSchedule(
            generated.GeneratorVersion, generated.Seed, generated.BlockCount, supplied);

        var expectedFirst = supplied[0];
        supplied[0] = supplied[0] with { Treatment = "C" };

        Assert.AreEqual(expectedFirst, schedule.Observations[0]);
        var writableView = (IList<ScheduledObservation>)schedule.Observations;
        Assert.ThrowsExactly<NotSupportedException>(
            () => writableView[0] = supplied[0]);
    }

    [TestMethod]
    public void SeedsOneAndTwoProduceDifferentBlockOrders()
    {
        var first = ExperimentScheduleGenerator.Generate(1, 8);
        var second = ExperimentScheduleGenerator.Generate(2, 8);

        CollectionAssert.AreNotEqual(CanonicalBytes(first), CanonicalBytes(second));
    }

    [TestMethod]
    public void ScheduleBalancesTreatmentsAcrossBlocksAndPositions()
    {
        var schedule = ExperimentScheduleGenerator.Generate(42, 8);
        var patterns = schedule.Observations
            .GroupBy(observation => observation.Block)
            .Select(block => string.Concat(block.Select(observation => observation.Treatment)))
            .ToArray();

        Assert.AreEqual(4, patterns.Count(pattern => pattern == "ABBA"));
        Assert.AreEqual(4, patterns.Count(pattern => pattern == "BAAB"));
        Assert.AreEqual(16, schedule.Observations.Count(observation => observation.Treatment == "A"));
        Assert.AreEqual(16, schedule.Observations.Count(observation => observation.Treatment == "B"));

        foreach (var position in Enumerable.Range(1, 4))
        {
            var observations = schedule.Observations.Where(observation => observation.Position == position);
            Assert.AreEqual(4, observations.Count(observation => observation.Treatment == "A"));
            Assert.AreEqual(4, observations.Count(observation => observation.Treatment == "B"));
        }
    }

    [TestMethod]
    [DataRow(42L, "ABBA,BAAB,ABBA,BAAB,BAAB,ABBA,BAAB,ABBA")]
    [DataRow(0L, "ABBA,BAAB,ABBA,ABBA,BAAB,BAAB,BAAB,ABBA")]
    [DataRow(-42L, "ABBA,BAAB,ABBA,ABBA,BAAB,BAAB,ABBA,BAAB")]
    public void GoldenSeedProducesExpectedBlockVector(long seed, string expected)
    {
        var schedule = ExperimentScheduleGenerator.Generate(seed, 8);
        var actual = string.Join(",", schedule.Observations
            .Chunk(4)
            .Select(block => string.Concat(block.Select(observation => observation.Treatment))));

        Assert.AreEqual(expected, actual);
        Assert.AreEqual("stryker-abba-baab-xorshift64star/v1", schedule.GeneratorVersion);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public void OddBlockCountIsRejected(int blockCount) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ExperimentScheduleGenerator.Generate(42, blockCount));

    [TestMethod]
    [DataRow(-2)]
    [DataRow(0)]
    [DataRow(10_002)]
    public void OutOfRangeBlockCountIsRejected(int blockCount) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => ExperimentScheduleGenerator.Generate(42, blockCount));

    private static byte[] CanonicalBytes(ExperimentSchedule schedule)
    {
        var lines = schedule.Observations.Select(observation => string.Join(
            ":",
            observation.ObservationId,
            observation.Sequence.ToString(CultureInfo.InvariantCulture),
            observation.Block.ToString(CultureInfo.InvariantCulture),
            observation.Position.ToString(CultureInfo.InvariantCulture),
            observation.Treatment));
        var canonical = string.Join(
            "\n",
            schedule.GeneratorVersion,
            schedule.Seed.ToString(CultureInfo.InvariantCulture),
            schedule.BlockCount.ToString(CultureInfo.InvariantCulture),
            string.Join("\n", lines));

        return Encoding.UTF8.GetBytes(canonical);
    }
}
