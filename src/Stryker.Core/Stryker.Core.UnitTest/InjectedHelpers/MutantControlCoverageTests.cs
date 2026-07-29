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
        HitMutant(1);
        HitMutant(2);
        for (var i = 0; i < 1000; i++)
        {
            HitMutant(1);
        }
        HitMutant(3);

        var coverage = GetCoverageData();

        coverage[0].ShouldBe(new[] { 1, 2, 3 });
        coverage[1].ShouldBeEmpty();
    }

    [TestMethod]
    public void ShouldRegisterMutantAgainAfterCoverageSnapshotReset()
    {
        HitMutant(1);
        using (EnterStaticContext())
        {
            HitMutant(2);
        }
        GetCoverageData()[0].ShouldBe(new[] { 1, 2 });

        // GetCoverageData resets the accumulated coverage, so an immediate second snapshot is empty
        var emptySnapshot = GetCoverageData();
        emptySnapshot[0].ShouldBeEmpty();
        emptySnapshot[1].ShouldBeEmpty();

        // and the same mutants must register again, in both the covered and the static lists
        HitMutant(1);
        using (EnterStaticContext())
        {
            HitMutant(2);
        }
        var coverage = GetCoverageData();
        coverage[0].ShouldBe(new[] { 1, 2 });
        coverage[1].ShouldBe(new[] { 2 });
    }

    [TestMethod]
    public void ShouldTrackStaticContextMutantsSeparately()
    {
        using (EnterStaticContext())
        {
            HitMutant(5);
            HitMutant(5);
        }
        HitMutant(6);

        var coverage = GetCoverageData();

        coverage[0].ShouldBe(new[] { 5, 6 });
        coverage[1].ShouldBe(new[] { 5 });
    }

    [TestMethod]
    public void ShouldPromoteMutantToStaticList_WhenFirstSeenOutsideStaticContext()
    {
        // A mutant first covered outside any static context, then hit during static
        // initialization, must appear in the static list as well - and only once in each list.
        HitMutant(7);
        using (EnterStaticContext())
        {
            HitMutant(7);
        }

        var coverage = GetCoverageData();

        coverage[0].ShouldBe(new[] { 7 });
        coverage[1].ShouldBe(new[] { 7 });
    }

    [TestMethod]
    public void ShouldRegisterCoverageConsistentlyUnderConcurrency()
    {
        const int distinctMutants = 200;
        Parallel.For(0, 4, _ =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                HitMutant(i % distinctMutants);
            }
        });

        var coverage = GetCoverageData();

        coverage[0].Count.ShouldBe(distinctMutants);
        coverage[0].Distinct().Count().ShouldBe(distinctMutants);
    }

    [TestMethod, Timeout(30000)]
    public void ShouldBlockSnapshotWhileCoverageLockIsHeld()
    {
        // The snapshot/reset must be serialized with registration through _coverageLock. Holding
        // that lock and observing that GetCoverageData cannot complete distinguishes the
        // synchronized implementation from one that snapshots without the lock.
        var lockField = _mutantControl.GetField("_coverageLock", BindingFlags.NonPublic | BindingFlags.Static)!;
        var coverageLock = lockField.GetValue(null)!;

        HitMutant(1);

        Monitor.Enter(coverageLock);
        Task<IList<int>[]> snapshot;
        try
        {
            snapshot = Task.Run(GetCoverageData);
            snapshot.Wait(300).ShouldBeFalse("GetCoverageData must not complete while the coverage lock is held");
        }
        finally
        {
            Monitor.Exit(coverageLock);
        }

        snapshot.Wait(10000).ShouldBeTrue("GetCoverageData must complete once the coverage lock is released");
        snapshot.Result[0].ShouldBe(new[] { 1 });
    }

    [TestMethod, Timeout(30000)]
    public void ShouldAssignEveryRegistrationToExactlyOneSnapshotGeneration()
    {
        // Bounded race: for each chunk the writer signals start, registers its ids from four threads
        // while the main thread takes one concurrent snapshot, then the next chunk begins. Every id
        // must appear in exactly one snapshot generation: the union is complete, and no snapshot
        // contains a duplicate.
        const int chunks = 20;
        const int idsPerChunk = 100;
        using var chunkStarted = new SemaphoreSlim(0);
        using var snapshotTaken = new SemaphoreSlim(0);

        var writer = Task.Run(() =>
        {
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var firstId = chunk * idsPerChunk;
                chunkStarted.Release();
                Parallel.For(0, 4, worker =>
                {
                    for (var i = worker; i < idsPerChunk; i += 4)
                    {
                        HitMutant(firstId + i);
                    }
                });
                snapshotTaken.Wait();
            }
        });

        var snapshots = new List<int[]>();
        for (var chunk = 0; chunk < chunks; chunk++)
        {
            chunkStarted.Wait();
            snapshots.Add(GetCoverageData()[0].ToArray());
            snapshotTaken.Release();
        }
        writer.GetAwaiter().GetResult();
        snapshots.Add(GetCoverageData()[0].ToArray());

        foreach (var snapshot in snapshots)
        {
            snapshot.Distinct().Count().ShouldBe(snapshot.Length, "a snapshot must not contain duplicate ids");
        }

        var union = snapshots.SelectMany(snapshot => snapshot).ToList();
        union.Count.ShouldBe(chunks * idsPerChunk, "every registration must belong to exactly one snapshot generation");
        union.Distinct().Count().ShouldBe(chunks * idsPerChunk);
    }

    [TestMethod]
    public void ShouldPublishAndResetCoverage_WhenFlushSucceeds()
    {
        var pathField = _mutantControl.GetField("_cachedCoverageFilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
        var cachedFlagField = _mutantControl.GetField("_coverageFilePathCached", BindingFlags.NonPublic | BindingFlags.Static)!;
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
            GetCoverageData()[0].ShouldBe(new[] { 11 }, "mutants must register again after a successful flush");
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
        // The MTP flush resets accumulated coverage only after a successful write; a failing write
        // must leave the accumulator intact so a later flush can still publish it.
        var pathField = _mutantControl.GetField("_cachedCoverageFilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
        var cachedFlagField = _mutantControl.GetField("_coverageFilePathCached", BindingFlags.NonPublic | BindingFlags.Static)!;
        var originalPath = pathField.GetValue(null);
        var originalFlag = cachedFlagField.GetValue(null);

        try
        {
            pathField.SetValue(null, Path.Combine(Path.GetTempPath(), "stryker-nonexistent-dir-" + Guid.NewGuid().ToString("N"), "coverage.txt"));
            cachedFlagField.SetValue(null, true);

            HitMutant(11);
            HitMutant(12);
            _mutantControl.GetMethod("FlushCoverageToFile").Invoke(null, null);

            GetCoverageData()[0].ShouldBe(new[] { 11, 12 }, "a failed flush must not clear accumulated coverage");
        }
        finally
        {
            pathField.SetValue(null, originalPath);
            cachedFlagField.SetValue(null, originalFlag);
        }
    }
}
