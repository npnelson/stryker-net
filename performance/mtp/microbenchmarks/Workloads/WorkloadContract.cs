using System.Collections.Immutable;

namespace Stryker.Performance.Mtp.Microbenchmarks;

internal enum TestFramework
{
    Xunit,
    Tunit,
}

internal enum WorkloadShape
{
    AllPass,
    OneFailure,
    PartialRun,
}

internal enum TestExecutionState
{
    Passed,
    Failed,
}

internal sealed record TestDescriptor(string Id, string DisplayName);

internal sealed record TestNodeUpdate(string TestId, TestExecutionState State);

internal sealed record WorkloadProfile(
    string Name,
    TestFramework Framework,
    WorkloadShape Shape,
    int TotalDiscoveredTests,
    ImmutableArray<TestDescriptor> Catalog,
    ImmutableArray<TestNodeUpdate> Updates);

internal sealed record WorkloadContract(ImmutableArray<WorkloadProfile> Profiles);
