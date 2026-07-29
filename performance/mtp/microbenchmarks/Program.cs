using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.ConsoleArguments.ListBenchmarks;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Stryker.Performance.Mtp.Microbenchmarks;

if (args is ["--validate-workloads"])
{
    try
    {
        var result = WorkloadContractValidator.Validate();
        WorkloadValidationReporter.WriteTo(Console.Out, result);
        return BenchmarkExitCode.Success;
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return BenchmarkExitCode.Failure;
    }
}

if (args is ["--verify-harness"])
{
    var summary = BenchmarkRunner.Run<HarnessVerificationBenchmark>(MtpBenchmarkConfig.CreateHarnessVerification());
    return BenchmarkExitCode.FromSummaries([summary], allowEmpty: false);
}

var config = MtpBenchmarkConfig.Create();
var (isParsingSuccess, _, options) = ConfigParser.Parse(args, ConsoleLogger.Default, config);

if (!isParsingSuccess)
{
    return BenchmarkExitCode.FromParseFailure(args);
}

var summaries = BenchmarkSwitcher
    .FromAssembly(typeof(HarnessVerificationBenchmark).Assembly)
    .Run(args, config);
var allowEmpty = options.PrintInformation ||
    options.ListBenchmarkCaseMode != ListBenchmarkCaseMode.Disabled;

return BenchmarkExitCode.FromSummaries(summaries, allowEmpty);
