using Microsoft.Extensions.Logging.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.TestRunner.MicrosoftTestPlatform.Models;
using Stryker.TestRunner.Results;
using Stryker.TestRunner.Tests;

namespace Stryker.TestRunner.MicrosoftTestPlatform.UnitTest;

internal class TestableRunner : SingleMicrosoftTestPlatformRunner
{
    private readonly Action _onDispose;
    private readonly Func<string, TestNode, string, Task<ICoverageRunResult>>? _coverageHandler;
    private readonly Func<string, IReadOnlyList<TestNode>, IReadOnlyList<string>, Task<IReadOnlyList<ICoverageRunResult>>>? _coverageCohortHandler;

    public TestableRunner(int id, Action onDispose)
        : base(id, new Dictionary<string, List<TestNode>>(),
                new Dictionary<string, MtpTestDescription>(),
                new TestSet(),
                new object(),
                NullLogger.Instance)
    {
        _onDispose = onDispose;
    }

    public TestableRunner(
        int id,
        Dictionary<string, List<TestNode>> testsByAssembly,
        Dictionary<string, MtpTestDescription> testDescriptions,
        TestSet testSet,
        object discoveryLock,
        Action onDispose,
        Func<string, TestNode, string, Task<ICoverageRunResult>>? coverageHandler = null,
        Func<string, IReadOnlyList<TestNode>, IReadOnlyList<string>, Task<IReadOnlyList<ICoverageRunResult>>>? coverageCohortHandler = null)
        : base(id, testsByAssembly, testDescriptions, testSet, discoveryLock, NullLogger.Instance)
    {
        _onDispose = onDispose;
        _coverageHandler = coverageHandler;
        _coverageCohortHandler = coverageCohortHandler;
    }

    internal override async Task<IReadOnlyList<ICoverageRunResult>> RunTestCohortForCoverageInReusedProcessAsync(
        string assembly,
        IReadOnlyList<TestNode> tests,
        IReadOnlyList<string> testIds)
    {
        if (_coverageCohortHandler is not null)
        {
            return await _coverageCohortHandler(assembly, tests, testIds).ConfigureAwait(false);
        }

        return await base.RunTestCohortForCoverageInReusedProcessAsync(assembly, tests, testIds).ConfigureAwait(false);
    }

    internal override async Task<ICoverageRunResult> RunSingleTestForCoverageInReusedProcessAsync(
        string assembly, TestNode test, string testId)
    {
        if (_coverageHandler is not null)
        {
            try
            {
                return await _coverageHandler(assembly, test, testId).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return CoverageRunResult.Create(testId, CoverageConfidence.Dubious,
                    Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
            }
        }

        return CoverageRunResult.Create(testId, CoverageConfidence.Normal,
            Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
    }

    public override void Dispose(bool disposing)
    {
        _onDispose?.Invoke();
        base.Dispose(disposing);
    }
}
