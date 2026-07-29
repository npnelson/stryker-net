using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace Stryker.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddExporter(JsonExporter.Full)
            .WithOption(ConfigOptions.JoinSummary, true)
            .WithOption(ConfigOptions.StopOnFirstError, true);

        var artifactsPath = Environment.GetEnvironmentVariable("BENCHMARK_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            config = config.WithArtifactsPath(artifactsPath);
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
