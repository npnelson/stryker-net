using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Stryker.Core.InjectedHelpers;

namespace Stryker.Core.UnitTest.InjectedHelpers;

/// <summary>
/// Behavioral tests for the injected MutantControl coverage accumulator. MutantControl is not
/// compiled into Stryker.Core (it is an embedded resource injected into mutated assemblies), so
/// these tests compile the helper sources the same way a mutated project would and drive the
/// resulting type through reflection.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MutantControlCoverageTests : TestBase
{
    private static Type _mutantControl;
    private static Type _mutantContext;

    [ClassInitialize]
    public static void CompileAndLoadHelpers(TestContext testContext)
    {
        // MutantControl maps the mutant-id file via MemoryMappedFile for the MTP runner; touch the type
        // so its defining assembly is loaded before the snapshot below and can be referenced.
        _ = typeof(System.IO.MemoryMappedFiles.MemoryMappedFile);

        var needed = new[] { ".CoreLib", ".Runtime", "System.IO.Pipes", "System.IO.MemoryMappedFiles", ".Collections", ".Console" };
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => needed.Any(name => assembly.FullName.Contains(name)) && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        var codeInjection = new CodeInjection();
        var syntaxTrees = codeInjection.MutantHelpers
            .Select(helper => CSharpSyntaxTree.ParseText(helper.Value, path: helper.Key))
            .ToList();

        var compilation = CSharpCompilation.Create("injected-helpers-under-test",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var memoryStream = new MemoryStream();
        var emitResult = compilation.Emit(memoryStream);
        emitResult.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));

        var assembly = Assembly.Load(memoryStream.ToArray());
        _mutantControl = assembly.GetType($"{codeInjection.HelperNamespace}.MutantControl");
        _mutantContext = assembly.GetType($"{codeInjection.HelperNamespace}.MutantContext");
        _mutantControl.ShouldNotBeNull();
        _mutantContext.ShouldNotBeNull();

        // Run the helper's static constructor while STRYKER_COVERAGE_FILE is guaranteed absent.
        // The constructor caches that variable, sets CaptureCoverage and registers a ProcessExit
        // flusher when it is present; if this suite ever runs inside an MTP coverage session (which
        // sets the variable for the host), an un-guarded test copy would flush its empty coverage
        // over the real injected helper's file on exit.
        var coverageFileVariable = Environment.GetEnvironmentVariable("STRYKER_COVERAGE_FILE");
        try
        {
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", null);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(_mutantControl.TypeHandle);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", coverageFileVariable);
        }
    }

    [TestInitialize]
    public void EnterCoverageMode()
    {
        _mutantControl.GetMethod("InitCoverage").Invoke(null, null);
        _mutantControl.GetField("CaptureCoverage").SetValue(null, true);
    }

    [TestCleanup]
    public void LeaveCoverageMode()
    {
        _mutantControl.GetField("CaptureCoverage").SetValue(null, false);
        _mutantControl.GetMethod("InitCoverage").Invoke(null, null);
    }

    private static void HitMutant(int id) => _mutantControl.GetMethod("IsActive").Invoke(null, new object[] { id });

    private static IList<int>[] GetCoverageData() => (IList<int>[])_mutantControl.GetMethod("GetCoverageData").Invoke(null, null);

    private static IDisposable EnterStaticContext() => (IDisposable)Activator.CreateInstance(_mutantContext);

    [TestMethod]
    public void ShouldRegisterEachCoveredMutantOnceInFirstSeenOrder()
    {
        HitMutant(10_003);
        HitMutant(250_019);
        for (var i = 0; i < 1000; i++)
        {
            HitMutant(10_003);
        }
        HitMutant(40_007);

        var coverage = GetCoverageData();

        coverage[0].ShouldBe(new[] { 10_003, 250_019, 40_007 });
        coverage[1].ShouldBeEmpty();
    }

    [TestMethod]
    public void ShouldRegisterMutantAgainAfterCoverageSnapshotReset()
    {
        HitMutant(101);
        using (EnterStaticContext())
        {
            HitMutant(202);
        }
        var firstSnapshot = GetCoverageData();

        HitMutant(202);
        using (EnterStaticContext())
        {
            HitMutant(101);
        }
        var secondSnapshot = GetCoverageData();

        firstSnapshot[0].ShouldBe(new[] { 101, 202 });
        firstSnapshot[1].ShouldBe(new[] { 202 });
        secondSnapshot[0].ShouldBe(new[] { 202, 101 });
        secondSnapshot[1].ShouldBe(new[] { 101 });
    }

    [TestMethod]
    public void ShouldTrackStaticContextMutantsOnceInFirstSeenOrder()
    {
        HitMutant(5);
        using (EnterStaticContext())
        {
            HitMutant(6);
            HitMutant(5);
            HitMutant(6);
            HitMutant(5);
        }

        var coverage = GetCoverageData();

        coverage[0].ShouldBe(new[] { 5, 6 });
        coverage[1].ShouldBe(new[] { 6, 5 });
    }

    [TestMethod]
    public void ShouldRegisterCoverageConsistentlyUnderConcurrency()
    {
        const int distinctMutants = 200;
        var expectedMutants = Enumerable.Range(1000, distinctMutants).ToArray();
        var expectedNormalMutants = expectedMutants.Where(id => id % 2 != 0).ToArray();
        var expectedStaticMutants = expectedMutants.Where(id => id % 2 == 0).ToArray();

        Parallel.For(0, 4, _ =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                HitMutant(expectedNormalMutants[i % expectedNormalMutants.Length]);
            }
            using (EnterStaticContext())
            {
                for (var i = 0; i < 10_000; i++)
                {
                    HitMutant(expectedStaticMutants[i % expectedStaticMutants.Length]);
                }
            }
        });

        var coverage = GetCoverageData();

        coverage[0].OrderBy(id => id).ToArray().ShouldBe(expectedMutants,
            "every expected normal id must appear exactly once");
        coverage[1].OrderBy(id => id).ToArray().ShouldBe(expectedStaticMutants,
            "only the expected static ids may appear, each exactly once");
    }

    [TestMethod, Timeout(30000)]
    public void ShouldIncludeRegistrationInSnapshot_WhenRegistrationLinearizesFirst()
    {
        // Deterministic generation-ordering proof. The test thread holds the coverage lock, so the
        // worker's snapshot cannot proceed; registrations made while the lock is held (Monitor is
        // reentrant) linearize BEFORE that snapshot. An implementation that snapshots without the
        // lock completes its snapshot before the registrations and returns empty lists instead.
        var coverageLock = GetCoverageLock();
        var getCoverageData = CreateWarmedSnapshotDelegate();

        var worker = new SnapshotWorker(getCoverageData);
        try
        {
            Monitor.Enter(coverageLock);
            try
            {
                worker.Start();
                // reach a definitive state either way - blocked (synchronized) or completed
                // (unsynchronized) - so the content assertions below do the discriminating
                worker.WaitUntilBlockedOrCompleted();
                HitMutant(42);
                using (EnterStaticContext())
                {
                    HitMutant(43);
                }
            }
            finally
            {
                Monitor.Exit(coverageLock);
            }

            var snapshot = worker.JoinAndGetResult();
            snapshot[0].ShouldBe(new[] { 42, 43 },
                "registrations made while holding the coverage lock must linearize before the blocked snapshot");
            snapshot[1].ShouldBe(new[] { 43 });

            var followingSnapshot = GetCoverageData();
            followingSnapshot[0].ShouldBeEmpty("normal coverage must not leak into the following generation");
            followingSnapshot[1].ShouldBeEmpty("static coverage must not leak into the following generation");
        }
        finally
        {
            worker.Drain();
        }
    }

    [TestMethod]
    public void ShouldPublishAndResetCoverage_WhenFlushSucceeds()
    {
        var pathField = _mutantControl.GetField("_cachedCoverageFilePath", BindingFlags.NonPublic | BindingFlags.Static);
        var cachedFlagField = _mutantControl.GetField("_coverageFilePathCached", BindingFlags.NonPublic | BindingFlags.Static);
        var originalPath = pathField.GetValue(null);
        var originalFlag = cachedFlagField.GetValue(null);
        var coverageFile = Path.Combine(Path.GetTempPath(), "stryker-flush-test-" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            pathField.SetValue(null, coverageFile);
            cachedFlagField.SetValue(null, true);

            HitMutant(11);
            using (EnterStaticContext())
            {
                HitMutant(12);
            }
            _mutantControl.GetMethod("FlushCoverageToFile").Invoke(null, null);

            File.ReadAllText(coverageFile).ShouldBe("11,12;12");

            // a successful flush resets the accumulator - both lists and their indexes
            var emptySnapshot = GetCoverageData();
            emptySnapshot[0].ShouldBeEmpty();
            emptySnapshot[1].ShouldBeEmpty();

            HitMutant(11);
            using (EnterStaticContext())
            {
                HitMutant(12);
            }
            var reRegistered = GetCoverageData();
            reRegistered[0].ShouldBe(new[] { 11, 12 }, "mutants must register again after a successful flush");
            reRegistered[1].ShouldBe(new[] { 12 }, "static mutants must register again after a successful flush");
        }
        finally
        {
            pathField.SetValue(null, originalPath);
            cachedFlagField.SetValue(null, originalFlag);
            if (File.Exists(coverageFile))
            {
                File.Delete(coverageFile);
            }
        }
    }

    [TestMethod]
    public void ShouldRetainCoverage_WhenCoverageFileWriteFails()
    {
        // The MTP flush resets accumulated coverage only after a successful write; a failed write
        // must leave the accumulator fully intact - the lists AND their membership indexes in
        // step. Mutants 10 (normal) and 13 (static) are sentinels that are NOT re-hit after the
        // failure: they can only appear in the retried output if the failed flush truly retained
        // state, so a buggy failure path that resets coverage cannot be papered over by the
        // re-hits. Re-hitting 11 and 12 catches an index-only reset as duplicates.
        var pathField = _mutantControl.GetField("_cachedCoverageFilePath", BindingFlags.NonPublic | BindingFlags.Static);
        var cachedFlagField = _mutantControl.GetField("_coverageFilePathCached", BindingFlags.NonPublic | BindingFlags.Static);
        var originalPath = pathField.GetValue(null);
        var originalFlag = cachedFlagField.GetValue(null);
        var invalidPath = Path.Combine(Path.GetTempPath(), "stryker-nonexistent-dir-" + Guid.NewGuid().ToString("N"), "coverage.txt");
        var retryPath = Path.Combine(Path.GetTempPath(), "stryker-flush-retry-" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            pathField.SetValue(null, invalidPath);
            cachedFlagField.SetValue(null, true);

            HitMutant(10);
            HitMutant(11);
            using (EnterStaticContext())
            {
                HitMutant(12);
                HitMutant(13);
            }
            _mutantControl.GetMethod("FlushCoverageToFile").Invoke(null, null);

            // re-hit only a subset; the sentinels 10 and 13 must survive on their own
            HitMutant(11);
            using (EnterStaticContext())
            {
                HitMutant(12);
            }

            pathField.SetValue(null, retryPath);
            _mutantControl.GetMethod("FlushCoverageToFile").Invoke(null, null);

            File.ReadAllText(retryPath).ShouldBe("10,11,12,13;12,13",
                "a failed flush must retain coverage - sentinels included - without desynchronizing the lists from their membership indexes");

            var afterRetry = GetCoverageData();
            afterRetry[0].ShouldBeEmpty("the successful retry must reset the accumulator");
            afterRetry[1].ShouldBeEmpty();
        }
        finally
        {
            pathField.SetValue(null, originalPath);
            cachedFlagField.SetValue(null, originalFlag);
            if (File.Exists(retryPath))
            {
                File.Delete(retryPath);
            }
        }
    }

    private static object GetCoverageLock() =>
        _mutantControl.GetField("_coverageLock", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static Func<IList<int>[]> CreateWarmedSnapshotDelegate()
    {
        // Strongly typed, warmed delegate: after warming, the only wait inside the in-test call is
        // the coverage lock, so an observed wait state is attributable to it.
        var getCoverageData = (Func<IList<int>[]>)_mutantControl.GetMethod("GetCoverageData")
            .CreateDelegate(typeof(Func<IList<int>[]>));
        getCoverageData();
        return getCoverageData;
    }

    /// <summary>
    /// Runs GetCoverageData on a dedicated background thread with bounded orchestration. ThreadState
    /// polling establishes that the worker either completed or is waiting on the held coverage lock
    /// before registration proceeds; returned snapshot contents remain the regression assertion.
    /// </summary>
    private sealed class SnapshotWorker
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _reachedInvocation = new(false);
        private IList<int>[] _result;
        private Exception _error;

        public SnapshotWorker(Func<IList<int>[]> getCoverageData) =>
            _thread = new Thread(() =>
            {
                try
                {
                    _reachedInvocation.Set();
                    _result = getCoverageData();
                }
                catch (Exception exception)
                {
                    _error = exception;
                }
            })
            {
                // a pathologically stuck worker must never keep the test host alive
                IsBackground = true
            };

        public void Start()
        {
            _thread.Start();
            _reachedInvocation.Wait(10000).ShouldBeTrue("the worker must reach the GetCoverageData invocation");
        }

        public void WaitUntilBlockedOrCompleted()
        {
            var deadline = Environment.TickCount64 + 10000;
            while (Environment.TickCount64 < deadline)
            {
                if (!_thread.IsAlive)
                {
                    return;
                }
                if ((_thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0)
                {
                    return;
                }
                Thread.Sleep(1);
            }

            Assert.Fail("the worker neither completed nor blocked within the deadline");
        }

        public IList<int>[] JoinAndGetResult()
        {
            _thread.Join(10000).ShouldBeTrue("the worker must complete once the coverage lock is released");
            if (_error is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_error).Throw();
            }
            _result.ShouldNotBeNull();
            return _result;
        }

        /// <summary>
        /// Failure-path cleanup: drains the worker so an assertion failure cannot leave it
        /// overlapping the next test's initialization (the coverage lock is released by then, so a
        /// healthy worker finishes promptly; a stuck one is a background thread and cannot block
        /// host shutdown).
        /// </summary>
        public void Drain()
        {
            var drained = !_thread.IsAlive || _thread.Join(2000);
            _reachedInvocation.Dispose();
            drained.ShouldBeTrue("the snapshot worker must drain once the coverage lock is released");
        }
    }
}
