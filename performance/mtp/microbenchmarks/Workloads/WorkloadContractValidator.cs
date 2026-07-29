using System.Collections.Immutable;
using System.Globalization;

namespace Stryker.Performance.Mtp.Microbenchmarks;

internal static class WorkloadContractValidator
{
    private const int ExpectedProfileCount = 6;
    private const int ExpectedTestCount = 128;
    private const int PartialRunUpdateCount = 16;
    private const string ExpectedContractHash = "11ab2f7fd05dbe34a9d6f87e18380655f6e250face0cdfb2b335757dda54486a";

    private static readonly IReadOnlyDictionary<string, string> ExpectedProfileHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["xunit-all-pass"] = "ebbb81fbfad2fb0adccc5377ca7ddf97d702864bedd8545d5d98dfeffce73f90",
            ["xunit-one-failure"] = "1d175b58039630e47b97f51e6979418f904b1626c8ebfeac8931578421dd43a2",
            ["xunit-partial-run"] = "70ea61b6a12bc9d379d62e75602fbbbb891bf88e860c8bc155b56bbce9c1a4af",
            ["tunit-all-pass"] = "3c5be09bfc42923778bc794ac52c41fafa2c89ee3f6255e4b7269b0f0dba90a1",
            ["tunit-one-failure"] = "53a436d6e667647f7a9b8e61882d063d2505d1fddd09b3ba1831741441529cc1",
            ["tunit-partial-run"] = "ab99a9fe7fda3614fb507cdbcf1db8f160e36e722ce2c896addf93d31af6551b",
        };

    public static WorkloadValidationResult Validate(WorkloadContract? contract = null)
    {
        contract ??= MtpWorkloadProfiles.Create();
        var errors = new List<string>();

        Require(
            contract.Profiles.Length == ExpectedProfileCount,
            $"Expected {ExpectedProfileCount} workload profiles, but found {contract.Profiles.Length}.",
            errors);

        foreach (var profile in contract.Profiles)
        {
            ValidateProfile(profile, errors);
        }

        ValidateFrameworkCatalogs(contract, TestFramework.Xunit, errors);
        ValidateFrameworkCatalogs(contract, TestFramework.Tunit, errors);

        var profileHashes = contract.Profiles.ToImmutableDictionary(
            static profile => profile.Name,
            WorkloadContractHasher.ComputeProfileHash,
            StringComparer.Ordinal);
        ValidateGoldenHashes(profileHashes, errors);

        var contractHash = WorkloadContractHasher.ComputeContractHash(contract);
        Require(
            contractHash == ExpectedContractHash,
            $"Contract hash changed. Expected '{ExpectedContractHash}', actual '{contractHash}'.",
            errors);

        var regeneratedHash = WorkloadContractHasher.ComputeContractHash(MtpWorkloadProfiles.Create());
        Require(
            contractHash == regeneratedHash,
            $"Workload generation is not deterministic. First hash '{contractHash}', second hash '{regeneratedHash}'.",
            errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Workload contract validation failed:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
        }

        return new WorkloadValidationResult(contract, profileHashes, contractHash);
    }

    private static void ValidateFrameworkCatalogs(
        WorkloadContract contract,
        TestFramework framework,
        ICollection<string> errors)
    {
        var profiles = contract.Profiles.Where(profile => profile.Framework == framework).ToArray();
        Require(profiles.Length == 3, $"Expected three {framework} profiles, but found {profiles.Length}.", errors);

        if (profiles.Length == 0)
        {
            return;
        }

        var expectedIds = profiles[0].Catalog.Select(static test => test.Id).ToArray();
        foreach (var profile in profiles.Skip(1))
        {
            Require(
                expectedIds.SequenceEqual(profile.Catalog.Select(static test => test.Id), StringComparer.Ordinal),
                $"Profile '{profile.Name}' does not reuse the canonical {framework} catalog.",
                errors);
        }

        if (framework == TestFramework.Xunit)
        {
            Require(
                expectedIds.All(static id => id.Length == 64),
                "Every xUnit test ID must be exactly 64 characters.",
                errors);
            Require(
                expectedIds.All(static id => id.All(static character =>
                    character is >= '0' and <= '9' or >= 'a' and <= 'f')),
                "Every xUnit test ID must be lowercase hexadecimal.",
                errors);
            return;
        }

        var lengths = expectedIds.Select(static id => id.Length).ToArray();
        Require(lengths.Min() == 133, $"TUnit minimum ID length must be 133, but was {lengths.Min()}.", errors);
        Require(lengths.Max() == 164, $"TUnit maximum ID length must be 164, but was {lengths.Max()}.", errors);
        Require(
            lengths.Sum() == 18_856,
            $"TUnit ID lengths must total 18856, but totaled {lengths.Sum()}.",
            errors);
        Require(
            lengths.Count(static length => length == 133) == 1 &&
            lengths.Count(static length => length == 147) == 89 &&
            lengths.Count(static length => length == 148) == 37 &&
            lengths.Count(static length => length == 164) == 1,
            "TUnit ID length distribution must be 1x133, 89x147, 37x148, and 1x164.",
            errors);
    }

    private static void ValidateGoldenHashes(
        IReadOnlyDictionary<string, string> actualHashes,
        ICollection<string> errors)
    {
        foreach (var expected in ExpectedProfileHashes)
        {
            Require(
                actualHashes.TryGetValue(expected.Key, out var actualHash),
                $"Missing expected profile '{expected.Key}'.",
                errors);

            if (actualHash is not null)
            {
                Require(
                    actualHash == expected.Value,
                    $"Profile '{expected.Key}' hash changed. Expected '{expected.Value}', actual '{actualHash}'.",
                    errors);
            }
        }
    }

    private static void ValidateProfile(WorkloadProfile profile, ICollection<string> errors)
    {
        Require(
            profile.TotalDiscoveredTests == ExpectedTestCount,
            $"Profile '{profile.Name}' must declare {ExpectedTestCount} discovered tests.",
            errors);
        Require(
            profile.Catalog.Length == ExpectedTestCount,
            $"Profile '{profile.Name}' must contain {ExpectedTestCount} catalog entries.",
            errors);
        Require(
            profile.Catalog.Select(static test => test.Id).Distinct(StringComparer.Ordinal).Count() == ExpectedTestCount,
            $"Profile '{profile.Name}' must contain {ExpectedTestCount} unique test IDs.",
            errors);

        var expectedUpdateCount = profile.Shape == WorkloadShape.PartialRun
            ? PartialRunUpdateCount
            : ExpectedTestCount;
        var expectedFailureCount = profile.Shape == WorkloadShape.AllPass ? 0 : 1;
        var expectedPassCount = expectedUpdateCount - expectedFailureCount;
        var catalogIds = profile.Catalog.Select(static test => test.Id).ToHashSet(StringComparer.Ordinal);

        Require(
            profile.Updates.Length == expectedUpdateCount,
            $"Profile '{profile.Name}' must contain {expectedUpdateCount} updates.",
            errors);
        Require(
            profile.Updates.Count(static update => update.State == TestExecutionState.Passed) == expectedPassCount,
            $"Profile '{profile.Name}' must contain {expectedPassCount} passed updates.",
            errors);
        Require(
            profile.Updates.Count(static update => update.State == TestExecutionState.Failed) == expectedFailureCount,
            $"Profile '{profile.Name}' must contain {expectedFailureCount} failed updates.",
            errors);
        Require(
            profile.Updates.Select(static update => update.TestId).Distinct(StringComparer.Ordinal).Count() == expectedUpdateCount,
            $"Profile '{profile.Name}' must not contain duplicate updates.",
            errors);
        Require(
            profile.Updates.All(update => catalogIds.Contains(update.TestId)),
            $"Profile '{profile.Name}' contains an update that is missing from its catalog.",
            errors);
    }

    private static void Require(bool condition, string message, ICollection<string> errors)
    {
        if (!condition)
        {
            errors.Add(message);
        }
    }
}

internal sealed record WorkloadValidationResult(
    WorkloadContract Contract,
    ImmutableDictionary<string, string> ProfileHashes,
    string ContractHash);

internal static class WorkloadValidationReporter
{
    public static void WriteTo(TextWriter writer, WorkloadValidationResult result)
    {
        writer.WriteLine($"Validated {result.Contract.Profiles.Length} deterministic workload profiles.");
        writer.WriteLine("xUnit IDs: 128 IDs, each exactly 64 characters.");
        writer.WriteLine("TUnit IDs: 128 IDs, range 133..164, mean 147.3125 characters.");

        foreach (var profile in result.Contract.Profiles)
        {
            var passed = profile.Updates.Count(static update => update.State == TestExecutionState.Passed);
            var failed = profile.Updates.Count(static update => update.State == TestExecutionState.Failed);
            var notRun = profile.TotalDiscoveredTests - profile.Updates.Length;
            writer.WriteLine(
                $"{profile.Name}: discovered={profile.TotalDiscoveredTests.ToString(CultureInfo.InvariantCulture)}, " +
                $"passed={passed.ToString(CultureInfo.InvariantCulture)}, " +
                $"failed={failed.ToString(CultureInfo.InvariantCulture)}, " +
                $"not-run={notRun.ToString(CultureInfo.InvariantCulture)}, " +
                $"sha256={result.ProfileHashes[profile.Name]}");
        }

        writer.WriteLine($"contract-sha256={result.ContractHash}");
    }
}
