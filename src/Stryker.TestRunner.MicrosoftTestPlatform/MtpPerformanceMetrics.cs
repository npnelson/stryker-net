namespace Stryker.TestRunner.MicrosoftTestPlatform;

internal enum MtpTestRunOutcome
{
    Completed,
    RpcDispatchTimeout,
    RunCompletionTimeout,
    Failed,
}

internal sealed class MtpPerformanceMetrics
{
    private long _leaseCount;
    private long _contendedLeaseCount;
    private long _totalQueueWaitTicks;
    private long _maxQueueWaitTicks;
    private long _totalRunnerBusyTicks;
    private long _maxRunnerBusyTicks;
    private int _maxQueueDepth;
    private long _hostStartCount;
    private long _hostStartFailureCount;
    private long _totalHostStartTicks;
    private long _maxHostStartTicks;
    private long _testRunCount;
    private long _completedTestRunCount;
    private long _failedTestRunCount;
    private long _rpcDispatchTimeoutCount;
    private long _runCompletionTimeoutCount;
    private long _totalRpcDispatchTicks;
    private long _maxRpcDispatchTicks;
    private long _totalRunCompletionTicks;
    private long _maxRunCompletionTicks;

    public void ObserveQueueDepth(int queueDepth) => SetMaximum(ref _maxQueueDepth, queueDepth);

    public void RecordLease(TimeSpan queueWait, TimeSpan runnerBusy, bool contended)
    {
        Interlocked.Increment(ref _leaseCount);
        if (contended)
        {
            Interlocked.Increment(ref _contendedLeaseCount);
            Interlocked.Add(ref _totalQueueWaitTicks, queueWait.Ticks);
            SetMaximum(ref _maxQueueWaitTicks, queueWait.Ticks);
        }

        Interlocked.Add(ref _totalRunnerBusyTicks, runnerBusy.Ticks);
        SetMaximum(ref _maxRunnerBusyTicks, runnerBusy.Ticks);
    }

    public void RecordHostStart(TimeSpan duration, bool succeeded)
    {
        Interlocked.Increment(ref _hostStartCount);
        if (!succeeded)
        {
            Interlocked.Increment(ref _hostStartFailureCount);
        }

        Interlocked.Add(ref _totalHostStartTicks, duration.Ticks);
        SetMaximum(ref _maxHostStartTicks, duration.Ticks);
    }

    public void RecordTestRun(TimeSpan rpcDispatch, TimeSpan runCompletion, MtpTestRunOutcome outcome)
    {
        Interlocked.Increment(ref _testRunCount);
        Interlocked.Add(ref _totalRpcDispatchTicks, rpcDispatch.Ticks);
        SetMaximum(ref _maxRpcDispatchTicks, rpcDispatch.Ticks);
        Interlocked.Add(ref _totalRunCompletionTicks, runCompletion.Ticks);
        SetMaximum(ref _maxRunCompletionTicks, runCompletion.Ticks);

        switch (outcome)
        {
            case MtpTestRunOutcome.Completed:
                Interlocked.Increment(ref _completedTestRunCount);
                break;
            case MtpTestRunOutcome.RpcDispatchTimeout:
                Interlocked.Increment(ref _rpcDispatchTimeoutCount);
                break;
            case MtpTestRunOutcome.RunCompletionTimeout:
                Interlocked.Increment(ref _runCompletionTimeoutCount);
                break;
            case MtpTestRunOutcome.Failed:
                Interlocked.Increment(ref _failedTestRunCount);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        }
    }

    public MtpPerformanceSnapshot Snapshot() => new(
        Interlocked.Read(ref _leaseCount),
        Interlocked.Read(ref _contendedLeaseCount),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalQueueWaitTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _maxQueueWaitTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalRunnerBusyTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _maxRunnerBusyTicks)),
        Volatile.Read(ref _maxQueueDepth),
        Interlocked.Read(ref _hostStartCount),
        Interlocked.Read(ref _hostStartFailureCount),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalHostStartTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _maxHostStartTicks)),
        Interlocked.Read(ref _testRunCount),
        Interlocked.Read(ref _completedTestRunCount),
        Interlocked.Read(ref _failedTestRunCount),
        Interlocked.Read(ref _rpcDispatchTimeoutCount),
        Interlocked.Read(ref _runCompletionTimeoutCount),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalRpcDispatchTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _maxRpcDispatchTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalRunCompletionTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _maxRunCompletionTicks)));

    private static void SetMaximum(ref long target, long candidate)
    {
        var current = Interlocked.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private static void SetMaximum(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

internal readonly record struct MtpPerformanceSnapshot(
    long LeaseCount,
    long ContendedLeaseCount,
    TimeSpan TotalQueueWait,
    TimeSpan MaxQueueWait,
    TimeSpan TotalRunnerBusy,
    TimeSpan MaxRunnerBusy,
    int MaxQueueDepth,
    long HostStartCount,
    long HostStartFailureCount,
    TimeSpan TotalHostStart,
    TimeSpan MaxHostStart,
    long TestRunCount,
    long CompletedTestRunCount,
    long FailedTestRunCount,
    long RpcDispatchTimeoutCount,
    long RunCompletionTimeoutCount,
    TimeSpan TotalRpcDispatch,
    TimeSpan MaxRpcDispatch,
    TimeSpan TotalRunCompletion,
    TimeSpan MaxRunCompletion)
{
    public MtpPerformanceSnapshot Add(MtpPerformanceSnapshot other) => new(
        LeaseCount + other.LeaseCount,
        ContendedLeaseCount + other.ContendedLeaseCount,
        TotalQueueWait + other.TotalQueueWait,
        MaxQueueWait > other.MaxQueueWait ? MaxQueueWait : other.MaxQueueWait,
        TotalRunnerBusy + other.TotalRunnerBusy,
        MaxRunnerBusy > other.MaxRunnerBusy ? MaxRunnerBusy : other.MaxRunnerBusy,
        Math.Max(MaxQueueDepth, other.MaxQueueDepth),
        HostStartCount + other.HostStartCount,
        HostStartFailureCount + other.HostStartFailureCount,
        TotalHostStart + other.TotalHostStart,
        MaxHostStart > other.MaxHostStart ? MaxHostStart : other.MaxHostStart,
        TestRunCount + other.TestRunCount,
        CompletedTestRunCount + other.CompletedTestRunCount,
        FailedTestRunCount + other.FailedTestRunCount,
        RpcDispatchTimeoutCount + other.RpcDispatchTimeoutCount,
        RunCompletionTimeoutCount + other.RunCompletionTimeoutCount,
        TotalRpcDispatch + other.TotalRpcDispatch,
        MaxRpcDispatch > other.MaxRpcDispatch ? MaxRpcDispatch : other.MaxRpcDispatch,
        TotalRunCompletion + other.TotalRunCompletion,
        MaxRunCompletion > other.MaxRunCompletion ? MaxRunCompletion : other.MaxRunCompletion);
}

internal readonly record struct MtpPerformanceSummary(
    TimeSpan RunnerCapacity,
    TimeSpan RunnerIdle,
    double RunnerUtilizationPercent)
{
    public static MtpPerformanceSummary Create(
        MtpPerformanceSnapshot snapshot,
        int runnerCount,
        TimeSpan elapsed)
    {
        var capacityTicks = elapsed.Ticks * (double)runnerCount;
        var busyTicks = snapshot.TotalRunnerBusy.Ticks;
        var idleTicks = Math.Max(0, capacityTicks - busyTicks);
        var utilizationPercent = capacityTicks <= 0
            ? 0
            : Math.Min(100, busyTicks / capacityTicks * 100);

        return new MtpPerformanceSummary(
            TimeSpan.FromTicks((long)Math.Min(long.MaxValue, capacityTicks)),
            TimeSpan.FromTicks((long)Math.Min(long.MaxValue, idleTicks)),
            utilizationPercent);
    }
}
