using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
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
