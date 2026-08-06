# Stryker verdict determinism on Polly

Measurements of released **dotnet-stryker 4.16.0** against **App-vNext/Polly**, using
Polly's own `eng/stryker-config.json` and its own pinned Stryker version.

## Why Polly

| | |
| --- | --- |
| Stryker version | `4.16.0`, pinned exactly in `.config/dotnet-tools.json` with `rollForward: false` |
| Runs on | every push and every pull request (`.github/workflows/mutation-tests.yml`) |
| Matrix | 5 jobs — Core, Extensions, Legacy, RateLimiting, Testing |
| Gate | `--break-at <MutationScore>`, and `<MutationScore>100</MutationScore>` in all five source projects |
| Runner | VsTest (xunit 2.9.3 + xunit.runner.visualstudio 3.1.5; xunit v2 has no MTP support) |

A gate at exactly 100 means any nondeterminism is a hard build failure, not a drifting
dashboard number. Nothing here uses a patched Stryker.

## Environment

4 cores, 15 GB, .NET SDK 10.0.110, Polly at `101d6af`. Polly's `global.json` pins SDK
`10.0.302`; it was repointed at the locally available `10.0.100`/`latestPatch` to build.
Every run below is `Polly.Core` from `test/Polly.Core.Tests`, `concurrency: 4` (Polly's
own setting) unless stated. Stryker reports 1480 mutants created, **358 tested**,
1122 skipped, against **698 tests**.

## Runs

| arm | score | Killed | Timeout | wall |
| --- | --- | --- | --- | --- |
| baseline run 1 | 100.00 % | 336 | 22 | 469 s |
| baseline run 2 | 100.00 % | 331 | 27 | 377 s |
| baseline run 3 | 100.00 % | 331 | 27 | 431 s |
| `additional-timeout: 30000` run a | 100.00 % | 337 | 21 | 750 s |
| `additional-timeout: 30000` run b | 100.00 % | 347 | 11 | 634 s |
| `disable-mix-mutants: true` | 100.00 % | 335 | 23 | 468 s |
| `concurrency: 1` run a | 100.00 % | 331 | 27 | 1426 s |
| `concurrency: 1` run b | 100.00 % | 334 | 24 | 1610 s |

## Finding 1 — the verdicts are not reproducible

Three identical baseline runs, same machine, same commit, same config:

```
Killed  Killed  Killed      320   <- stable
Timeout Timeout Timeout      15   <- stable
Killed  Killed  Timeout       8
Killed  Timeout Killed        5
Timeout Timeout Killed        4
Killed  Timeout Timeout       3
Timeout Killed  Killed        2
Timeout Killed  Timeout       1
                             23   unstable = 6.4% of the 358 tested
```

Over all six concurrency-4 runs, **41 mutants time out at least once and only 7 time out
every time** — 34 unstable, spread across 19 source files, so this is systemic rather
than one hot spot.

The reported score never moves, because `ProjectComponent.cs:53` scores both statuses the
same way:

```csharp
public IEnumerable<IReadOnlyMutant> DetectedMutants() => Mutants
    .Where(m => m.ResultStatus is MutantStatus.Killed or MutantStatus.Timeout);
```

Polly's gate is green because the instability happens to fall between two statuses that
both count as detected. Strip the 22 timeouts out of run 1's numerator and Polly.Core is
336/358 = **93.85 %**, a `--break-at 100` failure.

These runs show flapping between `Killed` and `Timeout` only. Whether the same instability
can reach `Survived` — which is what would actually break the build — is **not**
demonstrated here.

## Finding 2 — raising the timeout does not fix it, and costs 62 % wall clock

Apples to apples, two runs per arm:

| arm | pairwise verdict differences | wall clock |
| --- | --- | --- |
| baseline (5 s) | 11, 17, 18 — mean 15.3 | mean 426 s |
| `additional-timeout: 30000` | 14 | mean 692 s (**+62.6 %**) |
| `concurrency: 1` | 11 | mean 1518 s |
| `disable-mix-mutants` | 16–18 vs each baseline | 468 s |

Raising the timeout shifts the distribution (run b had 11 timeouts against the baseline's
22–27) without settling it. Dropping to concurrency 1 lowers instability to 11 of 358
(3.1 %) but does not remove it, so the cause is not purely CPU contention. Disabling
mutant grouping changes nothing.

A per-mutant race explains the shape: for a mutant that makes one covering test hang while
another covering test fails, the verdict depends on whether the failure is reported before
the session budget expires. A larger constant biases that race without deciding it.

## Finding 3 — static mutants are 21 % of the work and half the runtime

A mutant flagged `Static` is assessed against the whole suite
(`CoverageAnalyser.CoverageForThisMutant`):

```csharp
else if (resultTingRequirements.HasFlag(MutationTestingRequirements.Static) || mutant.IsStaticValue)
{
    // static mutations will be tested against every tests, except the one that are trusted not to cover it
    mutant.CoveringTests = allTestsGuidsExceptTrusted.Merge(testGuids);
    mutant.AssessingTests = allTestsGuidsExceptTrusted.Merge(assessingTests).Excluding(failedTest);
```

`testGuids` — the tests that actually executed the mutant during the coverage phase — is
computed and then discarded, because host reuse makes it unsafe to trust.

On Polly.Core, 74 of the 358 tested mutants are static. Counting test executions:

```
static (every test)   :  74  ->  74 x 698  = 51,652 test executions
non-static (covering) : 284  ->              6,269 test executions
                               (mean 22.1 covering tests, max 105)
static mutants are 20.7% of the work list but 89.2% of all test executions
```

Measured against the clock, from the `concurrency: 1` debug log — 358 mutants are tested
in 169 sessions, the 74 static ones alone and the other 284 grouped:

```
mutation phase (02:17:17 -> 02:39:53) : 1356 s
  whole-suite sessions :  74   644 s  mean 8.7 s  -> 47.5% of phase
  grouped sessions     :  95   712 s  mean 7.5 s  -> 52.5% of phase
```

**20.7 % of the mutants consume 47.5 % of the mutation phase.**

## Finding 4 — every whole-suite session shares one timeout budget

From the same debug log, all 74 whole-suite sessions use the identical value:

```
[02:18:20 DBG] Runner 0: Testing [515: Negate expression]
[02:18:20 DBG] Runner 0: Using 32702 ms as test run timeout
```

That is `DefaultTimeout`, computed once. The 95 grouped sessions use 71 distinct values
between 8277 ms and 29369 ms.

What the observed numbers pin down about `TimeoutValueCalculator`:

```csharp
_initializationTime = Math.Max(testSessionTime - aggregatedTestTimes, 0);
DefaultTimeout      = (_initializationTime + _aggregatedTestTimes) * Ratio + _extraMs;   // Ratio 1.5, _extraMs 5000
CalculateTimeoutValue(est) = (_initializationTime + est) * Ratio + _extraMs;
```

- `32702` gives `_initializationTime + _aggregatedTestTimes = 18468 ms`.
- the smallest observed group budget, `8277`, gives `_initializationTime + est = 2185 ms`,
  and `est >= 0`, so **`_initializationTime <= 2185 ms`** on this run.

So the initialization allowance is small, and a grouped session's budget is
`est * 1.5 + 5000` plus at most 2.2 s — where `est` is the summed *initial* runtime of that
group's covering tests. The whole-suite budget is unaffected by which mutant is running.

## Reproducing

```bash
python3 analyze.py verdicts/core-vstest-1.csv verdicts/core-vstest-2.csv verdicts/core-vstest-3.csv
python3 analyze.py verdicts/core-t30-a.csv   verdicts/core-t30-b.csv
python3 analyze.py verdicts/core-c1-a.csv    verdicts/core-c1-b.csv
```

The committed CSVs are per-mutant verdicts extracted by `extract.py` from each run's
`mutation-report.json` (2.2 MB apiece, almost all embedded source). `config-core*.json` are
the exact configs used; each is Polly's own config plus the one setting named in its
filename. To regenerate from scratch, clone Polly, `dotnet tool restore`, and run from
`test/Polly.Core.Tests`:

```bash
dotnet stryker --project Polly.Core.csproj --test-project Polly.Core.Tests.csproj \
  --config-file <config> --output <dir>
```

## Not established

- Whether the instability can produce `Survived`, which is what would fail Polly's gate.
- How much of this is specific to a resilience library whose tests are full of deliberate
  delays and fake time providers.
- The saving available from per-mutant process isolation. The 47.5 % cost is measured; the
  improvement is an inference. `--isolate-mutants` as built is MTP-only, and Polly's xunit
  v2 suite cannot run under MTP without migrating to xunit v3.
