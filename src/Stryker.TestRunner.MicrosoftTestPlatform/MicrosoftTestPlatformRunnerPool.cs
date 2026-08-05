using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;
using Stryker.TestRunner.MicrosoftTestPlatform.Models;
using Stryker.TestRunner.Results;
using Stryker.TestRunner.Tests;
using Stryker.Utilities.Logging;
using static Stryker.Abstractions.Testing.ITestRunner;

namespace Stryker.TestRunner.MicrosoftTestPlatform;

/// <summary>
/// Manages a pool of MicrosoftTestPlatformRunner instances to enable parallel mutation testing
/// with isolated environment variables per runner.
/// </summary>
public sealed class MicrosoftTestPlatformRunnerPool : ITestRunner
{
    private const int DefaultCoverageCohortSize = 1;
    private const int MaximumCoverageCohortSize = 1024;
    private readonly AutoResetEvent _runnerAvailableHandler = new(false);
    private readonly ConcurrentBag<SingleMicrosoftTestPlatformRunner> _availableRunners = new();
    private readonly ConcurrentBag<SingleMicrosoftTestPlatformRunner> _allRunners = new();
    private bool _disposed;
    private readonly ILogger _logger;
    private readonly int _countOfRunners;
    private readonly TestSet _testSet = new();
    private readonly Dictionary<string, List<TestNode>> _testsByAssembly = new();
    private readonly Dictionary<string, MtpTestDescription> _testDescriptions = new();
    private readonly object _discoveryLock = new();
    private readonly ISingleRunnerFactory _runnerFactory;
    private readonly IStrykerOptions _options;
    private readonly MtpPerformanceMetrics _leasePerformanceMetrics = new();
    private readonly IReadOnlyDictionary<MtpRunnerPhase, MtpPhasePerformanceMetrics> _phasePerformanceMetrics =
        Enum.GetValues<MtpRunnerPhase>().ToDictionary(phase => phase, _ => new MtpPhasePerformanceMetrics());
    private readonly Stopwatch _poolLifetime = new();
    private readonly int _coverageCohortSize;
    private int _waitingRunnerRequests;

    public IEnumerable<SingleMicrosoftTestPlatformRunner> Runners => _availableRunners;
    internal int CoverageCohortSize => _coverageCohortSize;

    public MicrosoftTestPlatformRunnerPool(IStrykerOptions options, ILogger? logger = null, ISingleRunnerFactory? runnerFactory = null)
        : this(options, logger, runnerFactory, options.CoverageCohortSize > 0 ? options.CoverageCohortSize : DefaultCoverageCohortSize)
    {
    }

    internal MicrosoftTestPlatformRunnerPool(
        IStrykerOptions options,
        ILogger? logger,
        ISingleRunnerFactory? runnerFactory,
        int coverageCohortSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(coverageCohortSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coverageCohortSize, MaximumCoverageCohortSize);

        _logger = logger ?? ApplicationLogging.LoggerFactory.CreateLogger<MicrosoftTestPlatformRunnerPool>();
        _options = options;
        _countOfRunners = Math.Max(1, options.Concurrency);
        _coverageCohortSize = coverageCohortSize;
        _runnerFactory = runnerFactory ?? new DefaultRunnerFactory();
        _logger.LogWarning("The Microsoft Test Platform testrunner is currently in preview. Results should be verified since this feature is still being tested.");
        if (_coverageCohortSize > 1)
        {
            _logger.LogWarning(
                "Experimental MTP coverage cohorts are enabled with up to {CoverageCohortSize} tests per request; coverage is conservatively shared by every test in a cohort",
                _coverageCohortSize);
        }

        Initialize();
        _poolLifetime.Start();
    }

    internal MtpPerformanceSnapshot PerformanceSnapshot
    {
        get
        {
            var snapshot = _leasePerformanceMetrics.Snapshot();
            foreach (var runner in _allRunners)
            {
                snapshot = snapshot.Add(runner.PerformanceSnapshot);
            }

            return snapshot;
        }
    }

    internal IReadOnlyDictionary<MtpRunnerPhase, MtpPhasePerformanceSnapshot> PhasePerformanceSnapshots =>
        _phasePerformanceMetrics
            .Select(pair => new KeyValuePair<MtpRunnerPhase, MtpPhasePerformanceSnapshot>(pair.Key, pair.Value.Snapshot()))
            .Where(pair => pair.Value.LeaseCount > 0)
            .ToDictionary();

    public void ResetTestProcesses()
    {
        _logger.LogDebug("Resetting all test server processes in the pool");
        var tasks = _availableRunners.Select(runner => runner.ResetServerAsync());
        Task.WhenAll(tasks).Wait();
        _logger.LogDebug("All test server processes have been reset");
    }

    private void Initialize()
    {
        // Create and initialize all runners in parallel to speed up startup time
        Parallel.For(0, _countOfRunners, (int i, ParallelLoopState _) =>
        {
            var runner = _runnerFactory.CreateRunner(
                i,
                _testsByAssembly,
                _testDescriptions,
                _testSet,
                _discoveryLock,
                _logger,
                _options);
            _availableRunners.Add(runner);
            _allRunners.Add(runner);
            _runnerAvailableHandler.Set();
        });
    }

    public async Task<bool> DiscoverTestsAsync(string assembly)
    {
        if (string.IsNullOrEmpty(assembly) || !File.Exists(assembly))
        {
            return false;
        }

        return await RunThisAsync(MtpRunnerPhase.Discovery, runner => runner.DiscoverTestsAsync(assembly)).ConfigureAwait(false);
    }

    public ITestSet GetTests(IProjectAndTests project) => _testSet;

    public async Task<ITestRunResult> InitialTestAsync(IProjectAndTests project)
    {
        var assemblies = project.GetTestAssemblies();
        if (!assemblies.Any())
        {
            return new TestRunResult(false, "No test assemblies found");
        }

        var results = await RunThisAsync(MtpRunnerPhase.InitialTest, runner => runner.InitialTestAsync(project)).ConfigureAwait(false);

        // reset all test processes after the initial test run
        ResetTestProcesses();

        return results;
    }

    public IEnumerable<ICoverageRunResult> CaptureCoverage(IProjectAndTests project)
    {
        // "perTestInIsolation" (CaptureCoveragePerTest) restarts the test process around every single
        // test so each result can be trusted with CoverageConfidence.Exact; "perTest" (CoverageBasedTest
        // only) reuses a warm process across tests, which is faster but can only ever earn
        // CoverageConfidence.Normal; "all" (SkipUncoveredMutants only) only needs to know which mutants
        // are covered by *some* test, so the cheaper aggregate capture is enough. Mirrors
        // VsTestRunnerPool's routing.
        if (_options.OptimizationMode.HasFlag(OptimizationModes.CaptureCoveragePerTest))
        {
            return CaptureCoveragePerIsolatedTests(project);
        }

        return _options.OptimizationMode.HasFlag(OptimizationModes.CoverageBasedTest)
            ? CaptureCoverageTestByTest(project)
            : CaptureCoverageInOneGo(project);
    }

    private IEnumerable<ICoverageRunResult> CaptureCoverageInOneGo(IProjectAndTests project)
    {
        _logger.LogInformation("Starting aggregate coverage capture for MTP runner");

        // Enable coverage mode on all runners
        foreach (var runner in _allRunners)
        {
            runner.SetCoverageMode(true);
        }

        try
        {
            // Run all tests with coverage tracking enabled
            var testResult = RunThisAsync(MtpRunnerPhase.AggregateCoverage, runner => runner.InitialTestAsync(project)).GetAwaiter().GetResult();

            if (testResult.FailingTests.IsEveryTest)
            {
                _logger.LogWarning("Coverage test run failed: {Message}", testResult.ResultMessage);
            }

            // Reset test processes to trigger coverage file flush (process exit writes coverage)
            ResetTestProcesses();

            // Aggregate coverage data from all runners
            var allCoveredMutants = new HashSet<int>();
            var allStaticMutants = new HashSet<int>();

            foreach (var runner in _availableRunners)
            {
                var (coveredMutants, staticMutants) = runner.ReadCoverageData();
                foreach (var mutantId in coveredMutants)
                {
                    allCoveredMutants.Add(mutantId);
                }
                foreach (var mutantId in staticMutants)
                {
                    allStaticMutants.Add(mutantId);
                }
            }

            _logger.LogInformation("Aggregate coverage capture complete: {CoveredCount} mutations covered, {StaticCount} static mutations",
                allCoveredMutants.Count, allStaticMutants.Count);

            // For cumulative coverage, we return a single coverage result that applies to all tests
            // Each test is assumed to cover all the mutations that were covered during the full test run
            // Static mutants are marked as such for proper handling during mutation testing
            return _testDescriptions.Values.Select(testDescription =>
                CoverageRunResult.Create(
                    testDescription.Id,
                    CoverageConfidence.Normal,
                    allCoveredMutants,
                    allStaticMutants,
                    []));
        }
        finally
        {
            // Disable coverage mode on all runners for subsequent mutation testing
            foreach (var runner in _availableRunners)
            {
                runner.SetCoverageMode(false);
            }
        }
    }

    /// <summary>
    /// Captures coverage one test at a time (each test's coverage is flushed and reset separately via
    /// the epoch relay in <see cref="SingleMicrosoftTestPlatformRunner.RunSingleTestForCoverageInReusedProcessAsync"/>),
    /// so each mutant's covering tests can be narrowed down instead of assuming every test covers it.
    /// Tests are distributed across the whole pool for parallelism; a runner keeps its per-assembly
    /// server warm across the tests it is handed rather than restarting it for every test.
    /// </summary>
    private IEnumerable<ICoverageRunResult> CaptureCoverageTestByTest(IProjectAndTests project)
    {
        _logger.LogInformation("Starting per-test coverage capture for MTP runner");

        foreach (var runner in _allRunners)
        {
            runner.SetPerTestCoverageMode(true);
        }

        try
        {
            var coverageCohorts = new List<(string Assembly, TestNode[] Tests, string[] TestIds)>();
            lock (_discoveryLock)
            {
                foreach (var (assembly, tests) in _testsByAssembly)
                {
                    var mappedTests = new List<(TestNode Test, string TestId)>();
                    foreach (var test in tests)
                    {
                        if (_testDescriptions.TryGetValue(test.Uid, out var description))
                        {
                            mappedTests.Add((test, description.Id));
                        }
                    }

                    foreach (var cohort in mappedTests.Chunk(_coverageCohortSize))
                    {
                        coverageCohorts.Add((
                            assembly,
                            cohort.Select(test => test.Test).ToArray(),
                            cohort.Select(test => test.TestId).ToArray()));
                    }
                }
            }

            _logger.LogInformation(
                "Capturing coverage for {TestCount} tests in {CohortCount} cohorts across {AssemblyCount} assemblies",
                coverageCohorts.Sum(cohort => cohort.Tests.Length),
                coverageCohorts.Count,
                _testsByAssembly.Count);

            var results = new ConcurrentBag<ICoverageRunResult>();

            Parallel.ForEach(coverageCohorts, new ParallelOptions { MaxDegreeOfParallelism = _countOfRunners }, cohort =>
            {
                if (_coverageCohortSize == 1)
                {
                    var result = RunThisAsync(
                            MtpRunnerPhase.PerTestCoverage,
                            runner => runner.RunSingleTestForCoverageInReusedProcessAsync(
                                cohort.Assembly,
                                cohort.Tests[0],
                                cohort.TestIds[0]))
                        .GetAwaiter().GetResult();
                    results.Add(result);
                    return;
                }

                var cohortResults = RunThisAsync(
                        MtpRunnerPhase.PerTestCoverage,
                        runner => runner.RunTestCohortForCoverageInReusedProcessAsync(
                            cohort.Assembly,
                            cohort.Tests,
                            cohort.TestIds))
                    .GetAwaiter().GetResult();
                foreach (var result in cohortResults)
                {
                    results.Add(result);
                }
            });

            _logger.LogInformation(
                "Coverage cohort capture complete: {TestCount} tests captured in {CohortCount} requests",
                results.Count,
                coverageCohorts.Count);

            return results;
        }
        finally
        {
            foreach (var runner in _availableRunners)
            {
                runner.SetPerTestCoverageMode(false);
            }
        }
    }

    /// <summary>
    /// Captures coverage one test at a time, restarting the test host process around each test (see
    /// <see cref="SingleMicrosoftTestPlatformRunner.RunSingleTestForCoverageInIsolatedProcessAsync"/>) so
    /// no state (static or otherwise) can leak between tests. This lets each result be trusted with
    /// <see cref="CoverageConfidence.Exact"/>, at the cost of a much slower init phase than the
    /// reused-process "perTest" capture in <see cref="CaptureCoverageTestByTest"/>.
    /// </summary>
    private IEnumerable<ICoverageRunResult> CaptureCoveragePerIsolatedTests(IProjectAndTests project)
    {
        _logger.LogInformation("Starting per-test-in-isolation coverage capture for MTP runner");

        foreach (var runner in _allRunners)
        {
            runner.SetCoverageMode(true);
        }

        try
        {
            var allTests = new List<(string Assembly, TestNode Test, string TestId)>();
            lock (_discoveryLock)
            {
                foreach (var (assembly, tests) in _testsByAssembly)
                {
                    foreach (var test in tests)
                    {
                        if (_testDescriptions.TryGetValue(test.Uid, out var description))
                        {
                            allTests.Add((assembly, test, description.Id));
                        }
                    }
                }
            }

            _logger.LogInformation("Capturing per-test-in-isolation coverage for {TestCount} tests across {AssemblyCount} assemblies",
                allTests.Count, _testsByAssembly.Count);

            var results = new ConcurrentBag<ICoverageRunResult>();

            Parallel.ForEach(allTests, new ParallelOptions { MaxDegreeOfParallelism = _countOfRunners }, testInfo =>
            {
                var result = RunThisAsync(MtpRunnerPhase.IsolatedCoverage, runner =>
                        runner.RunSingleTestForCoverageInIsolatedProcessAsync(testInfo.Assembly, testInfo.Test, testInfo.TestId))
                    .GetAwaiter().GetResult();
                results.Add(result);
            });

            _logger.LogInformation("Per-test-in-isolation coverage capture complete: {TestCount} tests captured", results.Count);

            return results;
        }
        finally
        {
            foreach (var runner in _availableRunners)
            {
                runner.SetCoverageMode(false);
            }
        }
    }

    public async Task<ITestRunResult> TestMultipleMutantsAsync(
        IProjectAndTests project,
        ITimeoutValueCalculator? timeoutCalc,
        IReadOnlyList<IMutant> mutants,
        TestUpdateHandler? update)
    {
        var assemblies = project.GetTestAssemblies();
        if (!assemblies.Any())
        {
            return new TestRunResult(false, "No test assemblies found");
        }

        return await RunThisAsync(MtpRunnerPhase.Mutation, runner => runner.TestMultipleMutantsAsync(project, timeoutCalc, mutants, update)).ConfigureAwait(false);
    }

    private async Task<T> RunThisAsync<T>(MtpRunnerPhase phase, Func<SingleMicrosoftTestPlatformRunner, Task<T>> task)
    {
        SingleMicrosoftTestPlatformRunner? runner;
        var leaseRequestStarted = Stopwatch.GetTimestamp();
        var contended = false;

        // Try to get a runner with a timeout to prevent indefinite blocking
        var attempts = 0;
        const int maxWaitTimeSeconds = 300; // 5 minutes max wait
        const int waitIntervalMs = 1000; // Check every second
        var maxAttempts = maxWaitTimeSeconds * 1000 / waitIntervalMs;

        if (!_availableRunners.TryTake(out runner))
        {
            contended = true;
            var queueDepth = Interlocked.Increment(ref _waitingRunnerRequests);
            _leasePerformanceMetrics.ObserveQueueDepth(queueDepth);
            _phasePerformanceMetrics[phase].ObserveQueueDepth(queueDepth);

            try
            {
                while (!_availableRunners.TryTake(out runner))
                {
                    if (!_runnerAvailableHandler.WaitOne(waitIntervalMs))
                    {
                        attempts++;
                        if (attempts >= maxAttempts)
                        {
                            throw new TimeoutException($"Timed out waiting for an available test runner after {maxWaitTimeSeconds} seconds. Available runners: {_availableRunners.Count}, Total runners: {_countOfRunners}");
                        }

                        if (attempts % 30 == 0) // Log every 30 seconds
                        {
                            _logger.LogWarning("Waiting for available test runner... ({Attempts}s elapsed, {Available}/{Total} runners available)",
                                attempts, _availableRunners.Count, _countOfRunners);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _waitingRunnerRequests);
            }
        }

        var queueWait = contended ? Stopwatch.GetElapsedTime(leaseRequestStarted) : TimeSpan.Zero;
        var runnerBusyStarted = Stopwatch.GetTimestamp();
        try
        {
            return await task(runner).ConfigureAwait(false);
        }
        finally
        {
            var leaseCompleted = Stopwatch.GetTimestamp();
            var runnerBusy = Stopwatch.GetElapsedTime(runnerBusyStarted, leaseCompleted);
            _leasePerformanceMetrics.RecordLease(
                queueWait,
                runnerBusy,
                contended);
            _phasePerformanceMetrics[phase].RecordLease(
                leaseRequestStarted,
                leaseCompleted,
                queueWait,
                runnerBusy,
                contended);
            _availableRunners.Add(runner);
            _runnerAvailableHandler.Set();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _poolLifetime.Stop();

        foreach (var runner in _allRunners)
        {
            runner.Dispose();
        }

        var snapshot = PerformanceSnapshot;
        var summary = MtpPerformanceSummary.Create(snapshot, _countOfRunners, _poolLifetime.Elapsed);
        _logger.LogInformation(
            "MTP performance summary: {RunnerCount} runners over {ElapsedMs} ms; {LeaseCount} leases ({ContendedLeaseCount} contended, max queue depth {MaxQueueDepth}, total queue wait {TotalQueueWaitMs} ms, max queue wait {MaxQueueWaitMs} ms); leased runner busy {RunnerBusyMs} ms, idle {RunnerIdleMs} ms, capacity {RunnerCapacityMs} ms, utilization {RunnerUtilizationPercent}%; {HostStartCount} host starts ({HostStartFailureCount} failed, total {HostStartMs} ms, max {MaxHostStartMs} ms); {TestRunCount} test runs ({CompletedTestRunCount} completed, {FailedTestRunCount} failed, {RpcDispatchTimeoutCount} RPC timeouts, {RunCompletionTimeoutCount} completion timeouts); RPC dispatch {RpcDispatchMs} ms total/{MaxRpcDispatchMs} ms max; run completion {RunCompletionMs} ms total/{MaxRunCompletionMs} ms max",
            _countOfRunners,
            _poolLifetime.Elapsed.TotalMilliseconds,
            snapshot.LeaseCount,
            snapshot.ContendedLeaseCount,
            snapshot.MaxQueueDepth,
            snapshot.TotalQueueWait.TotalMilliseconds,
            snapshot.MaxQueueWait.TotalMilliseconds,
            snapshot.TotalRunnerBusy.TotalMilliseconds,
            summary.RunnerIdle.TotalMilliseconds,
            summary.RunnerCapacity.TotalMilliseconds,
            summary.RunnerUtilizationPercent,
            snapshot.HostStartCount,
            snapshot.HostStartFailureCount,
            snapshot.TotalHostStart.TotalMilliseconds,
            snapshot.MaxHostStart.TotalMilliseconds,
            snapshot.TestRunCount,
            snapshot.CompletedTestRunCount,
            snapshot.FailedTestRunCount,
            snapshot.RpcDispatchTimeoutCount,
            snapshot.RunCompletionTimeoutCount,
            snapshot.TotalRpcDispatch.TotalMilliseconds,
            snapshot.MaxRpcDispatch.TotalMilliseconds,
            snapshot.TotalRunCompletion.TotalMilliseconds,
            snapshot.MaxRunCompletion.TotalMilliseconds);

        foreach (var (phase, phaseSnapshot) in PhasePerformanceSnapshots.OrderBy(pair => pair.Key))
        {
            var phaseSummary = MtpPerformanceSummary.Create(
                phaseSnapshot.TotalRunnerBusy,
                _countOfRunners,
                phaseSnapshot.Elapsed);
            _logger.LogInformation(
                "MTP phase performance: {Phase}; {LeaseCount} leases over {ElapsedMs} ms ({ContendedLeaseCount} contended, max queue depth {MaxQueueDepth}, total queue wait {TotalQueueWaitMs} ms, max queue wait {MaxQueueWaitMs} ms); leased runner busy {RunnerBusyMs} ms, idle {RunnerIdleMs} ms, capacity {RunnerCapacityMs} ms, utilization {RunnerUtilizationPercent}%",
                phase,
                phaseSnapshot.LeaseCount,
                phaseSnapshot.Elapsed.TotalMilliseconds,
                phaseSnapshot.ContendedLeaseCount,
                phaseSnapshot.MaxQueueDepth,
                phaseSnapshot.TotalQueueWait.TotalMilliseconds,
                phaseSnapshot.MaxQueueWait.TotalMilliseconds,
                phaseSnapshot.TotalRunnerBusy.TotalMilliseconds,
                phaseSummary.RunnerIdle.TotalMilliseconds,
                phaseSummary.RunnerCapacity.TotalMilliseconds,
                phaseSummary.RunnerUtilizationPercent);
        }
        _runnerAvailableHandler.Dispose();
    }
}

