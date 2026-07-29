using BenchmarkDotNet.Reports;

namespace Stryker.Performance.Mtp.Microbenchmarks;

internal static class BenchmarkExitCode
{
    public const int Failure = 1;
    public const int Success = 0;

    public static int FromParseFailure(IReadOnlyList<string> args) =>
        IsSingleInformationalRequest(args) ? Success : Failure;

    public static int FromSummaries(IEnumerable<Summary> summaries, bool allowEmpty)
    {
        var materializedSummaries = summaries.ToArray();

        if (materializedSummaries.Length == 0)
        {
            return allowEmpty ? Success : Failure;
        }

        return materializedSummaries.Any(static summary =>
            summary.HasCriticalValidationErrors ||
            summary.Reports.Length == 0 ||
            summary.Reports.Any(static report => !report.Success))
                ? Failure
                : Success;
    }

    private static bool IsSingleInformationalRequest(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            return false;
        }

        return args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("--version", StringComparison.OrdinalIgnoreCase);
    }
}
