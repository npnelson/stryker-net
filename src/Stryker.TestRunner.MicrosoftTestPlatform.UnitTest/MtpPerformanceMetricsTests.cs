using System.Diagnostics;
using Shouldly;

namespace Stryker.TestRunner.MicrosoftTestPlatform.UnitTest;

[TestClass]
public class MtpPerformanceMetricsTests
{
    [TestMethod]
    public void Snapshot_ShouldAggregateRunnerQueueHostAndRpcMeasurements()
    {
        var metrics = new MtpPerformanceMetrics();
        metrics.ObserveQueueDepth(2);
        metrics.ObserveQueueDepth(4);
        metrics.RecordLease(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(90), contended: true);
        metrics.RecordLease(TimeSpan.FromMilliseconds(999), TimeSpan.FromMilliseconds(60), contended: false);
        metrics.RecordHostStart(TimeSpan.FromMilliseconds(150), succeeded: true);
        metrics.RecordHostStart(TimeSpan.FromMilliseconds(300), succeeded: false);
        metrics.RecordTestRun(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(100),
            MtpTestRunOutcome.Completed);
        metrics.RecordTestRun(
            TimeSpan.FromMilliseconds(20),
            TimeSpan.Zero,
            MtpTestRunOutcome.RpcDispatchTimeout);
        metrics.RecordTestRun(
            TimeSpan.FromMilliseconds(7),
            TimeSpan.FromMilliseconds(120),
            MtpTestRunOutcome.RunCompletionTimeout);
        metrics.RecordTestRun(
            TimeSpan.FromMilliseconds(11),
            TimeSpan.FromMilliseconds(80),
            MtpTestRunOutcome.Failed);

        var snapshot = metrics.Snapshot();

        snapshot.LeaseCount.ShouldBe(2);
        snapshot.ContendedLeaseCount.ShouldBe(1);
        snapshot.TotalQueueWait.ShouldBe(TimeSpan.FromMilliseconds(40));
        snapshot.MaxQueueDepth.ShouldBe(4);
        snapshot.TotalRunnerBusy.ShouldBe(TimeSpan.FromMilliseconds(150));
        snapshot.HostStartCount.ShouldBe(2);
        snapshot.HostStartFailureCount.ShouldBe(1);
        snapshot.TestRunCount.ShouldBe(4);
        snapshot.CompletedTestRunCount.ShouldBe(1);
        snapshot.FailedTestRunCount.ShouldBe(1);
        snapshot.RpcDispatchTimeoutCount.ShouldBe(1);
        snapshot.RunCompletionTimeoutCount.ShouldBe(1);
        snapshot.TotalRpcDispatch.ShouldBe(TimeSpan.FromMilliseconds(43));
        snapshot.TotalRunCompletion.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [TestMethod]
    public void CreateSummary_ShouldCalculateCapacityIdleTimeAndUtilizationAcrossRunners()
    {
        var metrics = new MtpPerformanceMetrics();
        metrics.RecordLease(TimeSpan.Zero, TimeSpan.FromMilliseconds(150), contended: false);

        var summary = MtpPerformanceSummary.Create(
            metrics.Snapshot(),
            runnerCount: 2,
            elapsed: TimeSpan.FromSeconds(1));

        summary.RunnerCapacity.ShouldBe(TimeSpan.FromSeconds(2));
        summary.RunnerIdle.ShouldBe(TimeSpan.FromMilliseconds(1850));
        summary.RunnerUtilizationPercent.ShouldBe(7.5);
    }

    [TestMethod]
    public void PhaseSnapshot_ShouldTrackConcurrentLeaseSpanAndContention()
    {
        var metrics = new MtpPhasePerformanceMetrics();
        var origin = Stopwatch.GetTimestamp();
        metrics.ObserveQueueDepth(2);
        metrics.RecordLease(
            origin,
            origin + Stopwatch.Frequency,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(900),
            contended: true);
        metrics.RecordLease(
            origin + Stopwatch.Frequency / 2,
            origin + Stopwatch.Frequency * 2,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500),
            contended: false);

        var snapshot = metrics.Snapshot();
        var summary = MtpPerformanceSummary.Create(
            snapshot.TotalRunnerBusy, runnerCount: 2, snapshot.Elapsed);

        snapshot.LeaseCount.ShouldBe(2);
        snapshot.ContendedLeaseCount.ShouldBe(1);
        snapshot.MaxQueueDepth.ShouldBe(2);
        snapshot.Elapsed.ShouldBe(TimeSpan.FromSeconds(2));
        summary.RunnerUtilizationPercent.ShouldBe(35);
    }
}
