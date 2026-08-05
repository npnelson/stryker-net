using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.Testing;

namespace Stryker.Core.Initialisation;

public interface IInitialTestProcess
{
    Task<InitialTestRun> InitialTestAsync(IStrykerOptions options, IProjectAndTests project, ITestRunner testRunner);
}

public class InitialTestProcess : IInitialTestProcess
{
    private readonly ILogger _logger;

    public InitialTestProcess(ILogger<InitialTestProcess> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ITimeoutValueCalculator TimeoutValueCalculator { get; private set; }

    /// <summary>
    /// Executes the initial test run using the given testrunner
    /// </summary>
    /// <param name="project"></param>
    /// <param name="testRunner"></param>
    /// <param name="options">Stryker options</param>
    /// <returns>The duration of the initial test run</returns>
    public async Task<InitialTestRun> InitialTestAsync(IStrykerOptions options, IProjectAndTests project, ITestRunner testRunner)
    {
        // Setup a stopwatch to record the initial test duration
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var initTestRunResult = await testRunner.InitialTestAsync(project);
        // Stop stopwatch immediately after test run
        stopwatch.Stop();

        // timings
        _logger.LogDebug("Initial test run output: {ResultMessage}.", initTestRunResult.ResultMessage);

        var sessionTime = (int)stopwatch.ElapsedMilliseconds;
        var aggregatedTestTime = (int)initTestRunResult.Duration.TotalMilliseconds;

        TimeoutValueCalculator = new TimeoutValueCalculator(options.AdditionalTimeout, sessionTime, aggregatedTestTime);

        // The initialization allowance is inferred by subtraction (see TimeoutValueCalculator), so it is
        // only meaningful when the initial run executed its tests sequentially in one host. Surface the
        // inputs so a collapsed allowance is visible rather than silently inherited by every mutant.
        var initializationTime = Math.Max(sessionTime - aggregatedTestTime, 0);
        _logger.LogInformation(
            "Initial test run timings: wall clock {SessionTime} ms, aggregated test time {AggregatedTestTime} ms, "
            + "derived initialization allowance {InitializationTime} ms, default mutant timeout {DefaultTimeout} ms.",
            sessionTime, aggregatedTestTime, initializationTime, TimeoutValueCalculator.DefaultTimeout);

        if (sessionTime < aggregatedTestTime)
        {
            _logger.LogWarning(
                "Initial test run wall clock ({SessionTime} ms) is below its aggregated test time ({AggregatedTestTime} ms), "
                + "so the derived initialization allowance clamped to zero and mutant timeouts carry no startup budget. "
                + "This happens when the initial run executes tests concurrently or across several hosts; timeouts may fire spuriously.",
                sessionTime, aggregatedTestTime);
        }

        return new InitialTestRun(initTestRunResult, TimeoutValueCalculator);
    }
}
