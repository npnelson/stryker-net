using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Stryker.Performance.Mtp.Microbenchmarks;

internal static class MtpWorkloadProfiles
{
    private const int PartialRunUpdateCount = 16;
    private const int TestCount = 128;

    public static WorkloadContract Create()
    {
        var xunitCatalog = CreateXunitCatalog();
        var tunitCatalog = CreateTunitCatalog();

        return new WorkloadContract(
            ImmutableArray.Create(
                CreateProfile("xunit-all-pass", TestFramework.Xunit, WorkloadShape.AllPass, xunitCatalog),
                CreateProfile("xunit-one-failure", TestFramework.Xunit, WorkloadShape.OneFailure, xunitCatalog),
                CreateProfile("xunit-partial-run", TestFramework.Xunit, WorkloadShape.PartialRun, xunitCatalog),
                CreateProfile("tunit-all-pass", TestFramework.Tunit, WorkloadShape.AllPass, tunitCatalog),
                CreateProfile("tunit-one-failure", TestFramework.Tunit, WorkloadShape.OneFailure, tunitCatalog),
                CreateProfile("tunit-partial-run", TestFramework.Tunit, WorkloadShape.PartialRun, tunitCatalog)));
    }

    private static WorkloadProfile CreateProfile(
        string name,
        TestFramework framework,
        WorkloadShape shape,
        ImmutableArray<TestDescriptor> catalog)
    {
        var updateCount = shape == WorkloadShape.PartialRun ? PartialRunUpdateCount : TestCount;
        var failedIndex = shape switch
        {
            WorkloadShape.OneFailure => TestCount / 2,
            WorkloadShape.PartialRun => PartialRunUpdateCount - 1,
            _ => -1,
        };
        var updates = ImmutableArray.CreateBuilder<TestNodeUpdate>(updateCount);

        for (var index = 0; index < updateCount; index++)
        {
            var state = index == failedIndex ? TestExecutionState.Failed : TestExecutionState.Passed;
            updates.Add(new TestNodeUpdate(catalog[index].Id, state));
        }

        return new WorkloadProfile(name, framework, shape, TestCount, catalog, updates.MoveToImmutable());
    }

    private static ImmutableArray<TestDescriptor> CreateXunitCatalog()
    {
        var catalog = ImmutableArray.CreateBuilder<TestDescriptor>(TestCount);

        for (var index = 0; index < TestCount; index++)
        {
            var ordinal = index.ToString("D3", CultureInfo.InvariantCulture);
            var source = Encoding.UTF8.GetBytes($"xunit-mtp-benchmark-case-{ordinal}");
            var id = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
            catalog.Add(new TestDescriptor(id, $"XunitTheoryCases.Case[{ordinal}]"));
        }

        return catalog.MoveToImmutable();
    }

    private static ImmutableArray<TestDescriptor> CreateTunitCatalog()
    {
        var catalog = ImmutableArray.CreateBuilder<TestDescriptor>(TestCount);

        for (var index = 0; index < TestCount; index++)
        {
            var ordinal = index.ToString("D3", CultureInfo.InvariantCulture);
            var idLength = index switch
            {
                0 => 133,
                1 => 164,
                >= 2 and <= 38 => 148,
                _ => 147,
            };
            var id = CreateTunitId(index, ordinal, idLength);
            catalog.Add(new TestDescriptor(id, $"TUnitDataSourceCases.Case[{ordinal}]"));
        }

        return catalog.MoveToImmutable();
    }

    private static string CreateTunitId(int index, string ordinal, int targetLength)
    {
        var stem = $"Stryker.Performance.Mtp.Generated.TUnit.Case{ordinal}.";
        var tail = $"(System.Int32,System.String)#{ordinal}";
        var paddingLength = targetLength - stem.Length - tail.Length;

        if (paddingLength < 1)
        {
            throw new InvalidOperationException("The requested TUnit ID length cannot contain the deterministic ID shape.");
        }

        var padding = new string((char)('a' + (index % 26)), paddingLength);
        return string.Concat(stem, padding, tail);
    }
}
