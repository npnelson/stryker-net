using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Buildalyzer;
using Microsoft.Extensions.Logging;
using Stryker.Configuration;
using Stryker.Core.Helpers.ProcessUtil;
using Stryker.Utilities.Buildalyzer;
using Stryker.Utilities.Logging;

namespace Stryker.Core.Compiling;

/// <summary>
/// Experimental, opt-in (set <c>STRYKER_SANDBOX_R2R=1</c>) ReadyToRun (R2R) precompilation of the assemblies
/// Stryker deploys to the test projects' output folders (the mutation "sandbox").
/// <para>
/// Every freshly started test host pays a fixed JIT cost for the test framework, the test assembly and the
/// (mutated) assembly under test. Stryker starts many test hosts per run, so precompiling those assemblies
/// once — right after the mutated assembly has been injected — amortizes that cost over all test host launches.
/// </para>
/// <para>
/// This is best effort by design: any failure is logged as a warning and the assemblies simply stay regular IL,
/// it never fails the mutation test run. The crossgen2 compiler is located in the local NuGet cache and, when
/// missing, acquired once through <c>dotnet restore</c> (offline afterwards).
/// </para>
/// </summary>
public class ReadyToRunPrecompiler
{
    public const string EnableEnvironmentVariable = "STRYKER_SANDBOX_R2R";

    private const string Crossgen2PackPrefix = "microsoft.netcore.app.crossgen2";
    private const string TempFileSuffix = ".stryker-r2r-tmp";
    private const int MinimumSupportedMajorVersion = 6;
    private const int RestoreTimeoutMs = 10 * 60 * 1000;
    private const int CompileTimeoutMs = 5 * 60 * 1000;

    private readonly IFileSystem _fileSystem;
    private readonly IProcessExecutor _processExecutor;
    private readonly ILogger _logger;
    private readonly string _packagesRoot;
    private readonly string _sharedFrameworkRoot;

    public ReadyToRunPrecompiler(IFileSystem fileSystem = null,
        IProcessExecutor processExecutor = null,
        ILogger<ReadyToRunPrecompiler> logger = null,
        string packagesRoot = null,
        string sharedFrameworkRoot = null)
    {
        _fileSystem = fileSystem ?? new FileSystem();
        _processExecutor = processExecutor ?? new ProcessExecutor();
        _logger = logger ?? ApplicationLogging.LoggerFactory.CreateLogger<ReadyToRunPrecompiler>();
        _packagesRoot = packagesRoot
                        ?? Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        // when running framework dependent, the assembly containing System.Object lives in the shared framework
        // folder (e.g. <dotnet root>/shared/Microsoft.NETCore.App/<version>)
        _sharedFrameworkRoot = sharedFrameworkRoot
                               ?? Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location));
    }

    public static bool IsEnabled
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnableEnvironmentVariable);
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Precompiles the assemblies found in the output folders of the given test projects to ReadyToRun images.
    /// Never throws: on any failure the assemblies are left as (semantically identical) IL.
    /// </summary>
    /// <param name="testProjects">the analyzer results of the test projects making up the sandbox(es)</param>
    public void Precompile(IEnumerable<IAnalyzerResult> testProjects)
    {
        try
        {
            _logger.LogInformation("ReadyToRun sandbox precompilation is enabled ({EnvironmentVariable}=1).", EnableEnvironmentVariable);
            foreach (var testProjectsPerDirectory in testProjects.GroupBy(testProject => testProject.GetAssemblyDirectoryPath()))
            {
                PrecompileSandbox(testProjectsPerDirectory.Key, testProjectsPerDirectory.First());
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "ReadyToRun precompilation failed unexpectedly. Continuing with regular IL assemblies.");
        }
    }

    private void PrecompileSandbox(string assemblyDirectory, IAnalyzerResult testProject)
    {
        if (!IsSupported(testProject, out var targetMajorVersion, out var reason))
        {
            _logger.LogWarning("Skipping ReadyToRun precompilation for {AssemblyDirectory}: {Reason}", assemblyDirectory, reason);
            return;
        }

        var frameworkDirectory = ResolveSharedFrameworkDirectory(targetMajorVersion, out var frameworkVersion);
        if (frameworkDirectory is null)
        {
            _logger.LogWarning(
                "Skipping ReadyToRun precompilation for {AssemblyDirectory}: no installed Microsoft.NETCore.App shared framework matches major version {MajorVersion}.",
                assemblyDirectory, targetMajorVersion);
            return;
        }

        var crossgenPath = FindOrAcquireCrossgen2(frameworkVersion);
        if (crossgenPath is null)
        {
            _logger.LogWarning(
                "Skipping ReadyToRun precompilation for {AssemblyDirectory}: the crossgen2 compiler could not be acquired.",
                assemblyDirectory);
            return;
        }

        PrecompileDirectory(assemblyDirectory, crossgenPath, frameworkDirectory);
    }

    private static bool IsSupported(IAnalyzerResult testProject, out int targetMajorVersion, out string reason)
    {
        targetMajorVersion = 0;
        try
        {
            var framework = testProject.GetNuGetFramework();
            if (framework is null || framework.Framework != ".NETCoreApp" || framework.Version.Major < MinimumSupportedMajorVersion)
            {
                reason = $"only .NET {MinimumSupportedMajorVersion}+ test projects are supported (found '{testProject.TargetFramework}').";
                return false;
            }

            targetMajorVersion = framework.Version.Major;
        }
        catch (Exception)
        {
            reason = $"the target framework '{testProject.TargetFramework}' could not be parsed.";
            return false;
        }

        var platform = testProject.TargetPlatform();
        var processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
        if (!string.IsNullOrEmpty(platform)
            && !string.Equals(platform, "AnyCPU", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(platform, processArchitecture, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"the target platform '{platform}' does not match the current architecture '{processArchitecture}'.";
            return false;
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            reason = "the current operating system is not supported by crossgen2.";
            return false;
        }

        reason = null;
        return true;
    }

    private string ResolveSharedFrameworkDirectory(int targetMajorVersion, out Version frameworkVersion)
    {
        frameworkVersion = null;
        if (string.IsNullOrEmpty(_sharedFrameworkRoot) || !_fileSystem.Directory.Exists(_sharedFrameworkRoot))
        {
            return null;
        }

        var installed = _fileSystem.Directory.EnumerateDirectories(_sharedFrameworkRoot)
            .Select(directory => (Directory: directory, Version: ParseVersion(Path.GetFileName(directory))))
            .Where(candidate => candidate.Version is not null)
            .ToList();

        // prefer the highest patch level of the requested major version, else the lowest higher major
        // version (the version the test host will roll forward to)
        var best = installed.Where(candidate => candidate.Version.Major == targetMajorVersion)
                       .OrderByDescending(candidate => candidate.Version)
                       .Concat(installed.Where(candidate => candidate.Version.Major > targetMajorVersion)
                           .OrderBy(candidate => candidate.Version))
                       .FirstOrDefault();

        frameworkVersion = best.Version;
        return best.Directory;
    }

    private string FindOrAcquireCrossgen2(Version frameworkVersion)
    {
        var packName = $"{Crossgen2PackPrefix}.{RuntimeInformation.RuntimeIdentifier}";
        var crossgenPath = FindCachedCrossgen2(packName, frameworkVersion.Major);
        if (crossgenPath is not null)
        {
            _logger.LogDebug("Using cached crossgen2 compiler at {CrossgenPath}.", crossgenPath);
            return crossgenPath;
        }

        return AcquireCrossgen2(packName, frameworkVersion) ? FindCachedCrossgen2(packName, frameworkVersion.Major) : null;
    }

    private string FindCachedCrossgen2(string packName, int majorVersion)
    {
        var packRoot = Path.Combine(_packagesRoot, packName);
        if (!_fileSystem.Directory.Exists(packRoot))
        {
            return null;
        }

        var executableName = OperatingSystem.IsWindows() ? "crossgen2.exe" : "crossgen2";
        return _fileSystem.Directory.EnumerateDirectories(packRoot)
            .Select(directory => (Version: ParseVersion(Path.GetFileName(directory)), Path: Path.Combine(directory, "tools", executableName)))
            .Where(candidate => candidate.Version?.Major == majorVersion && _fileSystem.File.Exists(candidate.Path))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private bool AcquireCrossgen2(string packName, Version frameworkVersion)
    {
        var acquisitionDirectory = Path.Combine(Path.GetTempPath(), $"stryker-r2r-{Guid.NewGuid():N}");
        try
        {
            _fileSystem.Directory.CreateDirectory(acquisitionDirectory);
            // neutralize any Directory.Build/Directory.Packages files further up the temp path
            _fileSystem.File.WriteAllText(Path.Combine(acquisitionDirectory, "Directory.Build.props"), "<Project />");
            _fileSystem.File.WriteAllText(Path.Combine(acquisitionDirectory, "Directory.Build.targets"), "<Project />");
            _fileSystem.File.WriteAllText(Path.Combine(acquisitionDirectory, "Directory.Packages.props"), "<Project />");
            var projectPath = Path.Combine(acquisitionDirectory, "crossgen2-acquisition.csproj");
            _fileSystem.File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net{frameworkVersion.Major}.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageDownload Include="{packName}" Version="[{frameworkVersion}]" />
                  </ItemGroup>
                </Project>
                """);

            _logger.LogInformation("Acquiring the crossgen2 compiler ({PackName} {Version}) through dotnet restore.", packName, frameworkVersion);
            var stopwatch = Stopwatch.StartNew();
            var result = _processExecutor.Start(acquisitionDirectory, "dotnet", $"restore \"{projectPath}\"", timeoutMs: RestoreTimeoutMs);
            stopwatch.Stop();
            if (result.ExitCode != ExitCodes.Success)
            {
                _logger.LogWarning("Failed to acquire the crossgen2 compiler (dotnet restore exited with {ExitCode}): {Output}",
                    result.ExitCode, result.Output);
                return false;
            }

            _logger.LogInformation("Acquired the crossgen2 compiler in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timed out while acquiring the crossgen2 compiler.");
            return false;
        }
        finally
        {
            TryDeleteDirectory(acquisitionDirectory);
        }
    }

    private void PrecompileDirectory(string assemblyDirectory, string crossgenPath, string frameworkDirectory)
    {
        var stopwatch = Stopwatch.StartNew();
        var assemblies = _fileSystem.Directory.EnumerateFiles(assemblyDirectory, "*.dll")
            .Select(path => (Path: path, Inspection: InspectAssembly(path)))
            .Where(assembly => assembly.Inspection.IsManagedAssembly)
            .ToList();

        // already precompiled assemblies (e.g. from an earlier pass over a shared output folder) are valid
        // references but no longer candidates
        var candidates = assemblies.Where(assembly => !assembly.Inspection.HasNativeImage).Select(assembly => assembly.Path).ToList();
        if (candidates.Count == 0)
        {
            _logger.LogDebug("No assemblies to precompile in {AssemblyDirectory}.", assemblyDirectory);
            return;
        }

        var references = assemblies.Select(assembly => assembly.Path)
            .Concat(_fileSystem.Directory.EnumerateFiles(frameworkDirectory, "*.dll")
                .Where(path => InspectAssembly(path).IsManagedAssembly))
            .ToList();

        var precompiled = 0;
        foreach (var assembly in candidates)
        {
            if (PrecompileAssembly(assembly, crossgenPath, references))
            {
                precompiled++;
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "ReadyToRun precompiled {PrecompiledCount} out of {CandidateCount} sandbox assemblies in {AssemblyDirectory} in {ElapsedMs} ms.",
            precompiled, candidates.Count, assemblyDirectory, stopwatch.ElapsedMilliseconds);
    }

    private bool PrecompileAssembly(string assemblyPath, string crossgenPath, IReadOnlyCollection<string> references)
    {
        var temporaryOutput = assemblyPath + TempFileSuffix;
        var responseFilePath = Path.Combine(Path.GetTempPath(), $"stryker-r2r-{Guid.NewGuid():N}.rsp");
        try
        {
            var arguments = new List<string>
            {
                assemblyPath,
                $"-o:{temporaryOutput}",
                $"--targetos:{CurrentTargetOs}",
                $"--targetarch:{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}",
                "-O"
            };
            arguments.AddRange(references.Where(reference => reference != assemblyPath).Select(reference => $"-r:{reference}"));
            _fileSystem.File.WriteAllLines(responseFilePath, arguments);

            var result = _processExecutor.Start(Path.GetDirectoryName(assemblyPath), crossgenPath, $"\"@{responseFilePath}\"", timeoutMs: CompileTimeoutMs);
            if (result.ExitCode != ExitCodes.Success || !_fileSystem.File.Exists(temporaryOutput))
            {
                _logger.LogDebug("crossgen2 failed for {AssemblyPath} (exit code {ExitCode}): {Output} {Error}",
                    assemblyPath, result.ExitCode, result.Output, result.Error);
                return false;
            }

            _fileSystem.File.Move(temporaryOutput, assemblyPath, true);
            _logger.LogDebug("ReadyToRun precompiled {AssemblyPath}.", assemblyPath);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("crossgen2 timed out for {AssemblyPath}.", assemblyPath);
            return false;
        }
        finally
        {
            TryDeleteFile(responseFilePath);
            TryDeleteFile(temporaryOutput);
        }
    }

    private static string CurrentTargetOs => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private (bool IsManagedAssembly, bool HasNativeImage) InspectAssembly(string assemblyPath)
    {
        try
        {
            using var stream = _fileSystem.File.OpenRead(assemblyPath);
            using var reader = new PEReader(stream);
            if (!reader.HasMetadata)
            {
                return (false, false);
            }

            // a non empty managed native header indicates an existing ReadyToRun (or NGen) image
            return (true, reader.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0);
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return (false, false);
        }
    }

    private static Version ParseVersion(string value)
    {
        // strip any prerelease suffix (e.g. 10.0.0-preview.5) before parsing
        var dashIndex = value.IndexOf('-');
        return Version.TryParse(dashIndex < 0 ? value : value[..dashIndex], out var version) ? version : null;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // best effort cleanup
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (_fileSystem.Directory.Exists(path))
            {
                _fileSystem.Directory.Delete(path, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // best effort cleanup
        }
    }
}
