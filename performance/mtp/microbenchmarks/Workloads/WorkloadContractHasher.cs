using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Stryker.Performance.Mtp.Microbenchmarks;

internal static class WorkloadContractHasher
{
    public static string ComputeContractHash(WorkloadContract contract)
    {
        var canonical = new StringBuilder();

        foreach (var profile in contract.Profiles)
        {
            canonical
                .Append(profile.Name)
                .Append('|')
                .Append(ComputeProfileHash(profile))
                .Append('\n');
        }

        return ComputeHash(canonical.ToString());
    }

    public static string ComputeProfileHash(WorkloadProfile profile)
    {
        var canonical = new StringBuilder();
        canonical
            .Append(profile.Name)
            .Append('|')
            .Append(GetFrameworkToken(profile.Framework))
            .Append('|')
            .Append(GetShapeToken(profile.Shape))
            .Append('|')
            .Append(profile.TotalDiscoveredTests.ToString(CultureInfo.InvariantCulture))
            .Append('\n');

        foreach (var test in profile.Catalog)
        {
            canonical
                .Append("D|")
                .Append(test.Id)
                .Append('|')
                .Append(test.DisplayName)
                .Append('\n');
        }

        foreach (var update in profile.Updates)
        {
            canonical
                .Append("U|")
                .Append(update.TestId)
                .Append('|')
                .Append(GetStateToken(update.State))
                .Append('\n');
        }

        return ComputeHash(canonical.ToString());
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string GetFrameworkToken(TestFramework framework) => framework switch
    {
        TestFramework.Xunit => "xunit",
        TestFramework.Tunit => "tunit",
        _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null),
    };

    private static string GetShapeToken(WorkloadShape shape) => shape switch
    {
        WorkloadShape.AllPass => "all-pass",
        WorkloadShape.OneFailure => "one-failure",
        WorkloadShape.PartialRun => "partial-run",
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private static string GetStateToken(TestExecutionState state) => state switch
    {
        TestExecutionState.Passed => "passed",
        TestExecutionState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
