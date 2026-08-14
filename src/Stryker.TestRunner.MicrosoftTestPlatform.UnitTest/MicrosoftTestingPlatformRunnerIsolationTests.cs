using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Stryker.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.TestRunner.MicrosoftTestPlatform.Models;
using Stryker.TestRunner.Results;
using Stryker.TestRunner.Tests;

namespace Stryker.TestRunner.MicrosoftTestPlatform.UnitTest;

/// <summary>
/// Verifies that mutant sessions which cannot safely share a test host get isolated.
/// </summary>
[TestClass]
public class MicrosoftTestingPlatformRunnerIsolationTests
{
    private static IMutant CreateMutant(
        int id,
        bool isStaticValue = false,
        bool mustBeTestedInIsolation = false)
    {
        var mutant = new Mock<IMutant>();
        mutant.Setup(candidate => candidate.Id).Returns(id);
        mutant.Setup(candidate => candidate.IsStaticValue).Returns(isStaticValue);
        mutant.Setup(candidate => candidate.MustBeTestedInIsolation).Returns(mustBeTestedInIsolation);
        return mutant.Object;
    }

    private static IProjectAndTests CreateProject()
    {
        var project = new Mock<IProjectAndTests>();
        project.Setup(candidate => candidate.GetTestAssemblies()).Returns(["/test.dll"]);
        return project.Object;
    }

    [TestMethod, Timeout(1000)]
    public async Task TestMultipleMutantsAsync_StaticMutant_RunsOnDedicatedServer()
    {
        using var runner = new SessionTrackingRunner();

        await runner.TestMultipleMutantsAsync(
            CreateProject(), null, [CreateMutant(7, isStaticValue: true)], null);

        runner.Events.ShouldBe(["reset", "run:/test.dll", "reset"]);
        runner.ActiveMutantIds.ShouldBe([7]);
        runner.ReadMutantFile().ShouldBe(-1);
    }

    [TestMethod, Timeout(1000)]
    public async Task TestMultipleMutantsAsync_MutantMarkedByCoverage_RunsOnDedicatedServer()
    {
        using var runner = new SessionTrackingRunner();

        await runner.TestMultipleMutantsAsync(
            CreateProject(), null, [CreateMutant(7, mustBeTestedInIsolation: true)], null);

        runner.Events.ShouldBe(["reset", "run:/test.dll", "reset"]);
        runner.ActiveMutantIds.ShouldBe([7]);
    }

    [TestMethod, Timeout(1000)]
    public async Task TestMultipleMutantsAsync_RegularMutant_ReusesServer()
    {
        using var runner = new SessionTrackingRunner();

        await runner.TestMultipleMutantsAsync(CreateProject(), null, [CreateMutant(5)], null);

        runner.Events.ShouldBe(["run:/test.dll"]);
        runner.ActiveMutantIds.ShouldBe([5]);
    }

    [TestMethod, Timeout(1000)]
    public async Task TestMultipleMutantsAsync_Batch_RunsEachMutantActivatedInItsOwnSession()
    {
        using var runner = new SessionTrackingRunner();

        var result = await runner.TestMultipleMutantsAsync(
            CreateProject(), null, [CreateMutant(1), CreateMutant(2)], null);

        runner.Events.ShouldBe([
            "reset", "run:/test.dll", "reset",
            "reset", "run:/test.dll", "reset"]);
        runner.ActiveMutantIds.ShouldBe([1, 2]);
        result.FailingTests.GetIdentifiers().ShouldBe(["failed-1", "failed-2"], ignoreOrder: true);
    }

    [TestMethod, Timeout(1000)]
    public async Task TestMultipleMutantsAsync_CrashedBatch_ReturnsInconclusiveRuntimeError()
    {
        using var runner = new SessionTrackingRunner();
        runner.CrashOnRuns.Add(2);

        var result = await runner.TestMultipleMutantsAsync(
            CreateProject(), null, [CreateMutant(1), CreateMutant(2)], null);

        result.SessionHadRuntimeIssue.ShouldBeTrue();
        result.FailingTests.GetIdentifiers().ShouldBeEmpty();
        result.ExecutedTests.GetIdentifiers().ShouldBeEmpty();
        result.TimedOutTests.GetIdentifiers().ShouldBeEmpty();
    }

    [TestMethod, Timeout(1000)]
    public async Task InitialTestAsync_DoesNotResetServer()
    {
        using var runner = new SessionTrackingRunner();

        await runner.InitialTestAsync(CreateProject());

        runner.Events.ShouldBe(["run:/test.dll"]);
        runner.ActiveMutantIds.ShouldBe([-1]);
    }

    private sealed class SessionTrackingRunner : MicrosoftTestingPlatformRunner
    {
        public SessionTrackingRunner()
            : base(
                970,
                new Dictionary<string, List<TestNode>>(),
                new Dictionary<string, MtpTestDescription>(),
                new TestSet(),
                new object(),
                NullLogger.Instance)
        {
        }

        public List<string> Events { get; } = [];

        public List<int> ActiveMutantIds { get; } = [];

        public HashSet<int> CrashOnRuns { get; } = [];

        public int ReadMutantFile() => BitConverter.ToInt32(File.ReadAllBytes(MutantFilePath), 0);

        public override async Task ResetServerAsync()
        {
            Events.Add("reset");
            await base.ResetServerAsync();
        }

        internal override Task<(TestRunResult? Result, bool TimedOut, List<TestNode>? DiscoveredTests)>
            RunAssemblyTestsAsync(
                string assembly,
                ITimeoutValueCalculator? timeoutCalc,
                Func<TestNode, bool>? testUidFilter = null,
                bool bailOnFirstFailure = false)
        {
            Events.Add($"run:{assembly}");
            var activeMutantId = ReadMutantFile();
            ActiveMutantIds.Add(activeMutantId);

            if (CrashOnRuns.Contains(ActiveMutantIds.Count))
            {
                return Task.FromResult<(TestRunResult?, bool, List<TestNode>?)>(
                    (new TestRunResult(false, "simulated test host crash"), false, null));
            }

            var result = new TestRunResult(
                Array.Empty<IFrameworkTestDescription>(),
                TestIdentifierList.EveryTest(),
                new TestIdentifierList($"failed-{activeMutantId}"),
                TestIdentifierList.NoTest(),
                string.Empty,
                [],
                TimeSpan.Zero);
            return Task.FromResult<(TestRunResult?, bool, List<TestNode>?)>((result, false, null));
        }
    }
}
