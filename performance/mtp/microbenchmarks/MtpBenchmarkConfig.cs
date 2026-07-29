using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;

namespace Stryker.Performance.Mtp.Microbenchmarks;

internal static class MtpBenchmarkConfig
{
    public static IConfig Create() =>
        Create(
            Job.Default
                .WithRuntime(CoreRuntime.Core10_0)
                .WithId("net10.0")
                .AsDefault());

    public static IConfig CreateHarnessVerification() =>
        Create(
            Job.Dry
                .WithRuntime(CoreRuntime.Core10_0)
                .WithId("harness-verification"));

    private static IConfig Create(Job job)
    {
        var config = ManualConfig.CreateMinimumViable();

        config.AddJob(job);
        config.AddDiagnoser(MemoryDiagnoser.Default);
        config.AddExporter(JsonExporter.Full);
        config.AddExporter(CsvExporter.Default);
        config.AddExporter(MarkdownExporter.GitHub);

        return config;
    }
}
