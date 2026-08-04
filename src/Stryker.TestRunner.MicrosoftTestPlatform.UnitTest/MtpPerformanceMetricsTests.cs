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
        metrics.ObserveQueueDepth(3);
        metrics.RecordLease(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(90), contended: true);
        metrics.RecordLease(TimeSpan.FromMilliseconds(999), TimeSpan.FromMilliseconds(60), contended: false);
        metrics.RecordHostStart(TimeSpan.FromMilliseconds(150), succeeded: true);
        metrics.RecordHostStart(TimeSpan.FromMilliseconds(300), succeeded: false);
        metrics.RecordTestRun(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(100), MtpTestRunOutcome.Completed);
        metrics.RecordTestRun(TimeSpan.FromMilliseconds(20), TimeSpan.Zero, MtpTestRunOutcome.RpcDispatchTimeout);
        metrics.RecordTestRun(TimeSpan.FromMilliseconds(7), TimeSpan.FromMilliseconds(120), MtpTestRunOutcome.RunCompletionTimeout);
        metrics.RecordTestRun(TimeSpan.FromMilliseconds(11), TimeSpan.FromMilliseconds(80), MtpTestRunOutcome.Failed);

        var snapshot = metrics.Snapshot();

        snapshot.LeaseCount.ShouldBe(2);
        snapshot.ContendedLeaseCount.ShouldBe(1);
        snapshot.TotalQueueWait.ShouldBe(TimeSpan.FromMilliseconds(40));
        snapshot.MaxQueueWait.ShouldBe(TimeSpan.FromMilliseconds(40));
        snapshot.TotalRunnerBusy.ShouldBe(TimeSpan.FromMilliseconds(150));
        snapshot.MaxRunnerBusy.ShouldBe(TimeSpan.FromMilliseconds(90));
        snapshot.MaxQueueDepth.ShouldBe(4);
        snapshot.HostStartCount.ShouldBe(2);
        snapshot.HostStartFailureCount.ShouldBe(1);
        snapshot.TotalHostStart.ShouldBe(TimeSpan.FromMilliseconds(450));
        snapshot.MaxHostStart.ShouldBe(TimeSpan.FromMilliseconds(300));
        snapshot.TestRunCount.ShouldBe(4);
        snapshot.CompletedTestRunCount.ShouldBe(1);
        snapshot.FailedTestRunCount.ShouldBe(1);
        snapshot.RpcDispatchTimeoutCount.ShouldBe(1);
        snapshot.RunCompletionTimeoutCount.ShouldBe(1);
        snapshot.TotalRpcDispatch.ShouldBe(TimeSpan.FromMilliseconds(43));
        snapshot.MaxRpcDispatch.ShouldBe(TimeSpan.FromMilliseconds(20));
        snapshot.TotalRunCompletion.ShouldBe(TimeSpan.FromMilliseconds(300));
        snapshot.MaxRunCompletion.ShouldBe(TimeSpan.FromMilliseconds(120));
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
    public void Add_ShouldSumCountsAndDurationsWhileKeepingGlobalMaxima()
    {
        var first = new MtpPerformanceMetrics();
        first.ObserveQueueDepth(3);
        first.RecordLease(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(80), contended: true);
        first.RecordHostStart(TimeSpan.FromMilliseconds(200), succeeded: true);

        var second = new MtpPerformanceMetrics();
        second.ObserveQueueDepth(2);
        second.RecordLease(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(120), contended: true);
        second.RecordHostStart(TimeSpan.FromMilliseconds(100), succeeded: false);

        var combined = first.Snapshot().Add(second.Snapshot());

        combined.LeaseCount.ShouldBe(2);
        combined.TotalQueueWait.ShouldBe(TimeSpan.FromMilliseconds(30));
        combined.MaxQueueWait.ShouldBe(TimeSpan.FromMilliseconds(20));
        combined.TotalRunnerBusy.ShouldBe(TimeSpan.FromMilliseconds(200));
        combined.MaxRunnerBusy.ShouldBe(TimeSpan.FromMilliseconds(120));
        combined.MaxQueueDepth.ShouldBe(3);
        combined.HostStartCount.ShouldBe(2);
        combined.HostStartFailureCount.ShouldBe(1);
        combined.TotalHostStart.ShouldBe(TimeSpan.FromMilliseconds(300));
        combined.MaxHostStart.ShouldBe(TimeSpan.FromMilliseconds(200));
    }
}
