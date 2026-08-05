using System.Collections.Concurrent;

namespace TargetProject.StrykerFeatures;

/// <summary>
/// Reproduces the process-global caching pattern from stryker-mutator/stryker-net#3742.
///
/// The MTP runner reuses one test-host process across mutant sessions, switching the active
/// mutant through the control file. Anything the code under test memoises in static state is
/// computed once, under whichever mutant happened to be active at that moment, and is never
/// recomputed. Later sessions on the same host observe the earlier mutant's value.
///
/// Note that the mutated method here is an *instance* method in an ordinary (non-static)
/// context, so <c>IsStaticValue</c> does not flag it and the existing "run static mutants in a
/// dedicated test-server process" carve-out does not cover it. Only the cache it feeds is static.
/// </summary>
public class CachedRules
{
    // Process-global: populated on first use and never invalidated, exactly like CSLA's
    // static Lazy&lt;ConcurrentDictionary&lt;...&gt;&gt; rule cache.
    private static readonly ConcurrentDictionary<string, int> Cache = new();

    public int GetLimit(string key) => Cache.GetOrAdd(key, _ => ComputeLimit());

    /// <summary>
    /// Mutating this poisons <see cref="Cache"/> for the remaining life of the process.
    /// </summary>
    public int ComputeLimit() => 10 + 5;

    /// <summary>
    /// Mutating the literal here survives on a clean host: the assertion only checks that the
    /// rendered limit is present. On a host already poisoned by a <see cref="ComputeLimit"/>
    /// mutant it is reported killed instead.
    /// </summary>
    public string Describe() => "limit:" + GetLimit("default");
}
