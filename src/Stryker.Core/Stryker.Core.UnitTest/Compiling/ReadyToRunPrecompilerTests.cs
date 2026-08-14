using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using Stryker.Core.Compiling;
using Stryker.Core.Helpers.ProcessUtil;

namespace Stryker.Core.UnitTest.Compiling;

[TestClass]
public class ReadyToRunPrecompilerTests : TestBase
{
    private static readonly string BinDirectory = Path.Combine(Path.GetTempPath(), "stryker-r2r-tests", "bin");
    private static readonly string SharedFrameworkRoot = Path.Combine(Path.GetTempPath(), "stryker-r2r-tests", "shared");
    private static readonly string PackagesRoot = Path.Combine(Path.GetTempPath(), "stryker-r2r-tests", "packages");

    [TestMethod, DoNotParallelize]
    public void IsEnabledShouldReflectEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable(ReadyToRunPrecompiler.EnableEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ReadyToRunPrecompiler.EnableEnvironmentVariable, null);
            ReadyToRunPrecompiler.IsEnabled.ShouldBeFalse();
            Environment.SetEnvironmentVariable(ReadyToRunPrecompiler.EnableEnvironmentVariable, "1");
            ReadyToRunPrecompiler.IsEnabled.ShouldBeTrue();
            Environment.SetEnvironmentVariable(ReadyToRunPrecompiler.EnableEnvironmentVariable, "true");
            ReadyToRunPrecompiler.IsEnabled.ShouldBeTrue();
            Environment.SetEnvironmentVariable(ReadyToRunPrecompiler.EnableEnvironmentVariable, "0");
            ReadyToRunPrecompiler.IsEnabled.ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ReadyToRunPrecompiler.EnableEnvironmentVariable, original);
        }
    }

    [TestMethod]
    public void ShouldSkipFullFrameworkTestProjects()
    {
        var fileSystem = new MockFileSystem();
        var processExecutorMock = new Mock<IProcessExecutor>(MockBehavior.Strict);
        var target = CreatePrecompiler(fileSystem, processExecutorMock);

        target.Precompile([BuildTestProject("net472")]);

        VerifyNoProcessStarted(processExecutorMock);
    }

    [TestMethod]
    public void ShouldSkipTestProjectsTargetingAnotherPlatform()
    {
        var fileSystem = new MockFileSystem();
        var processExecutorMock = new Mock<IProcessExecutor>(MockBehavior.Strict);
        var target = CreatePrecompiler(fileSystem, processExecutorMock);

        // Itanium can never match the architecture the tests run on
        target.Precompile([BuildTestProject("net8.0", platform: "Itanium")]);

        VerifyNoProcessStarted(processExecutorMock);
    }

    [TestMethod]
    public void ShouldSkipWhenNoMatchingSharedFrameworkIsInstalled()
    {
        var fileSystem = new MockFileSystem();
        // only a .NET 6 shared framework is available while the test project targets .NET 8
        fileSystem.AddDirectory(Path.Combine(SharedFrameworkRoot, "6.0.36"));
        var processExecutorMock = new Mock<IProcessExecutor>(MockBehavior.Strict);
        var target = CreatePrecompiler(fileSystem, processExecutorMock);

        target.Precompile([BuildTestProject("net8.0")]);

        VerifyNoProcessStarted(processExecutorMock);
    }

    [TestMethod]
    public void ShouldFallBackToILWhenCrossgen2AcquisitionFails()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Path.Combine(SharedFrameworkRoot, "8.0.11"));
        var processExecutorMock = new Mock<IProcessExecutor>(MockBehavior.Strict);
        processExecutorMock.Setup(x => x.Start(It.IsAny<string>(), "dotnet", It.Is<string>(args => args.StartsWith("restore")),
                It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()))
            .Returns(new ProcessResult { ExitCode = 1, Output = "restore failed" });
        var target = CreatePrecompiler(fileSystem, processExecutorMock);

        target.Precompile([BuildTestProject("net8.0")]);

        // the failed acquisition should be the only process started, and the failure should not bubble up
        processExecutorMock.Verify(x => x.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()), Times.Once);
    }

    [TestMethod]
    public void ShouldFallBackToILWhenCrossgen2IsMissingAfterAcquisition()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Path.Combine(SharedFrameworkRoot, "8.0.11"));
        var processExecutorMock = new Mock<IProcessExecutor>(MockBehavior.Strict);
        // restore reports success but does not deliver the pack
        processExecutorMock.Setup(x => x.Start(It.IsAny<string>(), "dotnet", It.Is<string>(args => args.StartsWith("restore")),
                It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()))
            .Returns(new ProcessResult { ExitCode = 0, Output = string.Empty });
        var target = CreatePrecompiler(fileSystem, processExecutorMock);

        target.Precompile([BuildTestProject("net8.0")]);

        processExecutorMock.Verify(x => x.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()), Times.Once);
    }

    [TestMethod]
    public void ShouldPrecompileSandboxAssembliesWithCachedCrossgen2()
    {
        var managedAssemblyBytes = File.ReadAllBytes(typeof(ReadyToRunPrecompilerTests).Assembly.Location);
        var precompiledBytes = new byte[] { 0xC0, 0xFF, 0xEE };
        var crossgenPath = Path.Combine(PackagesRoot, $"microsoft.netcore.app.crossgen2.{RuntimeInformation.RuntimeIdentifier}",
            "8.0.11", "tools", OperatingSystem.IsWindows() ? "crossgen2.exe" : "crossgen2");
        var frameworkAssemblyPath = Path.Combine(SharedFrameworkRoot, "8.0.11", "System.Runtime.dll");
        var targetAssemblyPath = Path.Combine(BinDirectory, "Target.dll");

        var fileSystem = new MockFileSystem();
        fileSystem.AddDirectory(Path.GetTempPath());
        fileSystem.AddFile(crossgenPath, new MockFileData([0x4D, 0x5A]));
        fileSystem.AddFile(frameworkAssemblyPath, new MockFileData(File.ReadAllBytes(typeof(object).Assembly.Location)));
        fileSystem.AddFile(targetAssemblyPath, new MockFileData(managedAssemblyBytes));
        fileSystem.AddFile(Path.Combine(BinDirectory, "native.dll"), new MockFileData([0x00, 0x01, 0x02]));

        string[] responseFileContent = null;
        var processExecutorMock = new Mock<IProcessExecutor>(MockBehavior.Strict);
        processExecutorMock.Setup(x => x.Start(BinDirectory, crossgenPath, It.Is<string>(args => args.StartsWith("\"@")),
                It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()))
            .Returns((string _, string _, string arguments, IEnumerable<KeyValuePair<string, string>> _, int _) =>
            {
                // emulate crossgen2: read the response file and emit the requested output file
                var responseFilePath = arguments.Trim('"').TrimStart('@');
                responseFileContent = fileSystem.File.ReadAllLines(responseFilePath);
                var outputPath = responseFileContent.Single(line => line.StartsWith("-o:"))[3..];
                fileSystem.AddFile(outputPath, new MockFileData(precompiledBytes));
                return new ProcessResult { ExitCode = 0, Output = string.Empty };
            });
        var target = CreatePrecompiler(fileSystem, processExecutorMock);

        target.Precompile([BuildTestProject("net8.0")]);

        // the managed assembly should have been replaced by the (emulated) ReadyToRun image
        fileSystem.File.ReadAllBytes(targetAssemblyPath).ShouldBe(precompiledBytes);
        fileSystem.File.Exists(targetAssemblyPath + ".stryker-r2r-tmp").ShouldBeFalse();
        responseFileContent.ShouldNotBeNull();
        responseFileContent.ShouldContain(targetAssemblyPath);
        responseFileContent.ShouldContain("-O");
        responseFileContent.ShouldContain($"-r:{frameworkAssemblyPath}");
        // the non managed file is neither compiled nor used as a reference
        responseFileContent.ShouldNotContain(line => line.Contains("native.dll"));
        processExecutorMock.Verify(x => x.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()), Times.Once);
    }

    private static ReadyToRunPrecompiler CreatePrecompiler(MockFileSystem fileSystem, Mock<IProcessExecutor> processExecutorMock) =>
        new(fileSystem, processExecutorMock.Object, TestLoggerFactory.CreateLogger<ReadyToRunPrecompiler>(),
            packagesRoot: PackagesRoot, sharedFrameworkRoot: SharedFrameworkRoot);

    private static Buildalyzer.IAnalyzerResult BuildTestProject(string targetFramework, string platform = null)
    {
        var properties = new Dictionary<string, string>
        {
            { "TargetDir", BinDirectory },
            { "TargetFileName", "Target.dll" }
        };
        if (platform is not null)
        {
            properties["TargetPlatform"] = platform;
        }

        return TestHelper.SetupProjectAnalyzerResult(properties: properties, targetFramework: targetFramework).Object;
    }

    private static void VerifyNoProcessStarted(Mock<IProcessExecutor> processExecutorMock) =>
        processExecutorMock.Verify(x => x.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IEnumerable<KeyValuePair<string, string>>>(), It.IsAny<int>()), Times.Never);
}
