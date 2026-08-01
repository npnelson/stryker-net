using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using Stryker.Core.InjectedHelpers;

namespace Stryker.Core.UnitTest.InjectedHelpers;

[TestClass]
public class InjectedHelperTests : TestBase
{
    [TestMethod]
    [DataRow(LanguageVersion.CSharp2)]
    [DataRow(LanguageVersion.CSharp3)]
    [DataRow(LanguageVersion.CSharp4)]
    [DataRow(LanguageVersion.CSharp5)]
    [DataRow(LanguageVersion.CSharp6)]
    [DataRow(LanguageVersion.CSharp7)]
    [DataRow(LanguageVersion.CSharp7_1)]
    [DataRow(LanguageVersion.CSharp7_2)]
    [DataRow(LanguageVersion.CSharp7_3)]
    [DataRow(LanguageVersion.CSharp8)]
    [DataRow(LanguageVersion.Default)]
    [DataRow(LanguageVersion.Latest)]
    [DataRow(LanguageVersion.LatestMajor)]
    [DataRow(LanguageVersion.Preview)]
    public void InjectHelpers_ShouldCompile_ForAllLanguageVersions(LanguageVersion version)
    {
        // MutantControl maps the mutant-id file via MemoryMappedFile for the MTP runner; touch the type
        // so its defining assembly is loaded before the snapshot below and can be referenced.
        _ = typeof(System.IO.MemoryMappedFiles.MemoryMappedFile);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var needed = new[] { ".CoreLib", ".Runtime", "System.IO.Pipes", "System.IO.MemoryMappedFiles", ".Collections", ".Console" };
        var references = new List<MetadataReference>();
        foreach (var assembly in assemblies)
        {
            if (needed.Any(x => assembly.FullName.Contains(x)))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        var syntaxes = new List<SyntaxTree>();
        var codeInjection = new CodeInjection();

        foreach (var helper in codeInjection.MutantHelpers)
        {
            syntaxes.Add(CSharpSyntaxTree.ParseText(helper.Value, new CSharpParseOptions(languageVersion: version),
                helper.Key));
        }

        var compilation = CSharpCompilation.Create("dummy.dll",
            syntaxes,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            references: references);

        compilation.GetDiagnostics().ShouldNotContain(diag => diag.Severity == DiagnosticSeverity.Error,
            $"errors :{string.Join(Environment.NewLine, compilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Select(diag => $"{diag.Id}: '{diag.GetMessage()}' at {diag.Location.SourceTree.FilePath}, {diag.Location.GetLineSpan().StartLinePosition.Line + 1}:{diag.Location.GetLineSpan().StartLinePosition.Character}"))}");
    }

    [TestMethod]
    [DataRow(LanguageVersion.CSharp8)]
    [DataRow(LanguageVersion.CSharp9)]
    [DataRow(LanguageVersion.CSharp10)]
    [DataRow(LanguageVersion.CSharp11)]
    [DataRow(LanguageVersion.CSharp12)]
    [DataRow(LanguageVersion.Default)]
    [DataRow(LanguageVersion.Latest)]
    [DataRow(LanguageVersion.LatestMajor)]
    [DataRow(LanguageVersion.Preview)]
    public void InjectHelpers_ShouldCompile_ForAllLanguageVersionsWithNullableOptions(LanguageVersion version)
    {
        // MutantControl maps the mutant-id file via MemoryMappedFile for the MTP runner; touch the type
        // so its defining assembly is loaded before the snapshot below and can be referenced.
        _ = typeof(System.IO.MemoryMappedFiles.MemoryMappedFile);

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var needed = new[] { ".CoreLib", ".Runtime", "System.IO.Pipes", "System.IO.MemoryMappedFiles", ".Collections", ".Console" };
        var references = new List<MetadataReference>();
        var hack = new NamedPipeClientStream("test");
        foreach (var assembly in assemblies)
        {
            if (needed.Any(x => assembly.FullName.Contains(x)))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }

        var syntaxes = new List<SyntaxTree>();
        var codeInjection = new CodeInjection();

        foreach (var helper in codeInjection.MutantHelpers)
        {
            syntaxes.Add(CSharpSyntaxTree.ParseText(helper.Value, new CSharpParseOptions(languageVersion: version),
                helper.Key));
        }

        var compilation = CSharpCompilation.Create("dummy.dll",
            syntaxes,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            references: references);

        compilation.GetDiagnostics().ShouldNotContain(diag => diag.Severity == DiagnosticSeverity.Error,
            $"errors :{string.Join(Environment.NewLine, compilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Select(diag => $"{diag.Id}: '{diag.GetMessage()}' at {diag.Location.SourceTree.FilePath}, {diag.Location.GetLineSpan().StartLinePosition.Line + 1}:{diag.Location.GetLineSpan().StartLinePosition.Character}"))}");
    }

    [TestMethod]
    public void MutantControl_ShouldKeepEveryCopysCoverage_WhenSharingOneCoverageFile()
    {
        // A test host loads one compiled copy of the injected MutantControl per mutated assembly (a
        // solution with two mutated projects produces two), and the MTP runner hands the host a
        // single STRYKER_COVERAGE_FILE. Every copy's flush must end up in that file: a flush that is
        // lost means the assembly's covered mutants are reported NoCoverage and never tested.
        var coverageFileName = $"stryker-coverage-injected-helper-test-{Environment.ProcessId}.txt";
        var coverageFilePath = Path.Combine(Path.GetTempPath(), coverageFileName);
        Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", coverageFileName);

        try
        {
            var copyA = CompileMutantControlCopy("MutatedAssemblyA");
            var copyB = CompileMutantControlCopy("MutatedAssemblyB");

            copyA.IsActive(1001); // mutated code in assembly A runs during a test
            copyB.IsActive(2001); // mutated code in assembly B runs during the same test

            copyA.FlushCoverageToFile();
            copyB.FlushCoverageToFile();

            var content = File.ReadAllText(coverageFilePath);
            content.ShouldContain("1001", customMessage: "assembly A's coverage was lost when assembly B flushed");
            content.ShouldContain("2001");
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", null);
            if (File.Exists(coverageFilePath))
            {
                File.Delete(coverageFilePath);
            }
        }
    }

    [TestMethod]
    public void MutantControl_ShouldKeepAMidSessionCopysCoverage_ForTheNextEpoch()
    {
        // Per-test capture keeps the test host alive and relays "publish your coverage" through an
        // 8-byte epoch file (request int, ack int). A copy whose assembly first executes mid-session
        // wakes to an epoch that is already acknowledged; whatever the relay does at that moment, the
        // coverage the copy registered must still reach the next epoch's read - it belongs to the test
        // that is running right now.
        var suffix = $"injected-helper-epoch-test-{Environment.ProcessId}";
        var coverageFileName = $"stryker-coverage-{suffix}.txt";
        var coverageFilePath = Path.Combine(Path.GetTempPath(), coverageFileName);
        var epochFileName = $"stryker-epoch-{suffix}.txt";
        var epochFilePath = Path.Combine(Path.GetTempPath(), epochFileName);
        File.WriteAllBytes(epochFilePath, new byte[8]); // request=0 / ack=0, as the runner initializes it
        Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", coverageFileName);
        Environment.SetEnvironmentVariable("STRYKER_COVERAGE_EPOCH_FILE", epochFileName);

        try
        {
            var copyA = CompileMutantControlCopy("EpochAssemblyA");
            copyA.IsActive(1001); // assembly A runs during test 1; its epoch poller starts

            WriteEpochRequest(epochFilePath, 1);
            WaitUntil(() => ReadEpochAck(epochFilePath) == 1, "epoch 1 was never acknowledged");
            WaitUntil(() => CoverageFileContains(coverageFilePath, "1001"), "copy A's flush for epoch 1 never arrived");

            var copyB = CompileMutantControlCopy("EpochAssemblyB");
            copyB.IsActive(2001); // assembly B first executes mid-session: epoch 1 is already acknowledged

            // Give B's poller (1ms cadence) ample time for its first observation. If the relay handles
            // the already-acknowledged epoch by flushing, that flush becomes visible here; if it stays
            // silent, the grace period simply elapses. Either way the schedule is fixed before epoch 2.
            var graceDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
            while (DateTime.UtcNow < graceDeadline && !CoverageFileContains(coverageFilePath, "2001"))
            {
                Thread.Sleep(5);
            }

            // The runner clears the per-test file before each request so surviving lines belong to one test
            File.Delete(coverageFilePath);
            WriteEpochRequest(epochFilePath, 2);
            WaitUntil(() => ReadEpochAck(epochFilePath) == 2, "epoch 2 was never acknowledged");
            // The ack does not say which copy answered, so allow B's own append time to land
            var settleDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
            while (DateTime.UtcNow < settleDeadline && !CoverageFileContains(coverageFilePath, "2001"))
            {
                Thread.Sleep(5);
            }

            CoverageFileContains(coverageFilePath, "2001").ShouldBeTrue(
                "a copy whose assembly first runs mid-session must keep its coverage for the next epoch's read");
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", null);
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_EPOCH_FILE", null);
            if (File.Exists(coverageFilePath))
            {
                File.Delete(coverageFilePath);
            }
            if (File.Exists(epochFilePath))
            {
                File.Delete(epochFilePath);
            }
        }
    }

    private static void WaitUntil(Func<bool> condition, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            Thread.Sleep(5);
        }
        condition().ShouldBeTrue(timeoutMessage);
    }

    private static bool CoverageFileContains(string coverageFilePath, string mutantId)
        => File.Exists(coverageFilePath) && File.ReadAllText(coverageFilePath).Contains(mutantId);

    [TestMethod]
    public void MutantControl_ShouldNotAcknowledgeAnEpoch_WhenTheCoverageFlushFailed()
    {
        // The runner treats ack == requested epoch as "the coverage file now holds this test's data" and
        // reads it. So the ack has to mean the flush landed: if a write fails, the file still holds the
        // PREVIOUS test's content, and acknowledging anyway credits this test with that content.
        var suffix = $"injected-helper-failed-flush-{Environment.ProcessId}";
        var coverageFileName = $"stryker-coverage-{suffix}.txt";
        var coverageFilePath = Path.Combine(Path.GetTempPath(), coverageFileName);
        var epochFileName = $"stryker-epoch-{suffix}.txt";
        var epochFilePath = Path.Combine(Path.GetTempPath(), epochFileName);

        // A directory at the coverage path makes every write fail, standing in for the real-world causes
        // (a lock held by a scanner, a read-only or full temp volume).
        Directory.CreateDirectory(coverageFilePath);
        File.WriteAllBytes(epochFilePath, new byte[8]);
        Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", coverageFileName);
        Environment.SetEnvironmentVariable("STRYKER_COVERAGE_EPOCH_FILE", epochFileName);

        try
        {
            var copy = CompileMutantControlCopy("FailedFlushAssembly");
            copy.IsActive(1001);

            WriteEpochRequest(epochFilePath, 1);
            // Give the poller (1ms cadence) ample time to observe the request and attempt its flush.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < deadline && ReadEpochAck(epochFilePath) != 1)
            {
                Thread.Sleep(5);
            }

            ReadEpochAck(epochFilePath).ShouldNotBe(1,
                "the epoch must not be acknowledged when the coverage flush failed - the runner reads the file on this signal");
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_FILE", null);
            Environment.SetEnvironmentVariable("STRYKER_COVERAGE_EPOCH_FILE", null);
            if (Directory.Exists(coverageFilePath))
            {
                Directory.Delete(coverageFilePath, true);
            }
            if (File.Exists(epochFilePath))
            {
                File.Delete(epochFilePath);
            }
        }
    }

    private static void WriteEpochRequest(string epochFilePath, int epoch)
    {
        using var stream = new FileStream(epochFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        stream.Write(BitConverter.GetBytes(epoch), 0, 4);
    }

    private static int ReadEpochAck(string epochFilePath)
    {
        using var stream = new FileStream(epochFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(4, SeekOrigin.Begin);
        var buffer = new byte[4];
        stream.ReadExactly(buffer, 0, 4);
        return BitConverter.ToInt32(buffer, 0);
    }

    private sealed record MutantControlCopy(MethodInfo IsActiveMethod, MethodInfo FlushMethod)
    {
        public void IsActive(int id) => IsActiveMethod.Invoke(null, new object[] { id });
        public void FlushCoverageToFile() => FlushMethod.Invoke(null, null);
    }

    private static MutantControlCopy CompileMutantControlCopy(string assemblyName)
    {
        // Compile the injected helpers the same way InjectHelpers_ShouldCompile does, then load the
        // result: each load yields a distinct Stryker.MutantControl type with its own static state,
        // exactly like the per-assembly copies injection produces.
        _ = typeof(System.IO.MemoryMappedFiles.MemoryMappedFile);
        _ = new NamedPipeClientStream("test");

        var needed = new[] { ".CoreLib", ".Runtime", "System.IO.Pipes", "System.IO.MemoryMappedFiles", ".Collections", ".Console" };
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => needed.Any(x => assembly.FullName.Contains(x)))
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        var syntaxes = new CodeInjection().MutantHelpers
            .Select(helper => (SyntaxTree)CSharpSyntaxTree.ParseText(helper.Value, path: helper.Key))
            .ToList();

        var compilation = CSharpCompilation.Create($"{assemblyName}.dll",
            syntaxes,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            references: references);

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.ShouldBeTrue(string.Join(Environment.NewLine,
            emitResult.Diagnostics.Where(diag => diag.Severity == DiagnosticSeverity.Error)));

        // injection rewrites the helper namespace per project, so resolve by simple type name
        var mutantControl = Assembly.Load(stream.ToArray()).GetTypes().Single(type => type.Name == "MutantControl");
        return new MutantControlCopy(
            mutantControl.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Static),
            mutantControl.GetMethod("FlushCoverageToFile", BindingFlags.Public | BindingFlags.Static));
    }
}
