using System;
using System.Collections.Generic;

namespace Stryker.Benchmarks;

internal static class CoverageBenchmarkEnvironment
{
    public static void Start()
    {
        MutantControl.CaptureCoverage = true;
        MutantControl.ResetCoverage();
        ValidateCoverageContract();
    }

    public static IList<int>[] FinishGeneration() => MutantControl.GetCoverageData();

    public static void Stop()
    {
        MutantControl.ResetCoverage();
        MutantControl.CaptureCoverage = false;
    }

    public static int[] BuildRepeatedAscendingHits(int distinctMutants, int passes)
    {
        var hits = new int[checked(distinctMutants * passes)];
        for (var i = 0; i < hits.Length; i++)
        {
            hits[i] = i % distinctMutants;
        }

        return hits;
    }

    private static void ValidateCoverageContract()
    {
        MutantControl.IsActive(1);
        MutantControl.IsActive(1);
        using (new MutantContext())
        {
            MutantControl.IsActive(2);
        }

        var coverage = MutantControl.GetCoverageData();
        if (coverage[0].Count != 2 ||
            coverage[0][0] != 1 ||
            coverage[0][1] != 2 ||
            coverage[1].Count != 1 ||
            coverage[1][0] != 2)
        {
            throw new InvalidOperationException(
                "The selected MutantControl source does not preserve the expected coverage contract.");
        }
    }
}
