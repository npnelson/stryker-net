# Where the MTP runner's time actually goes on Polly.Core

All runs: Polly `codex/xunit3-mtp2-benchmark` @ `7f276902`, Stryker built from this branch (the
strong-name fix), `--test-runner mtp --concurrency 1`, 698 tests, 4-core box.

## Question

The `Utils/ObjectPool.cs` slice took ~12 s per mutation run and 8 of its 9 mutants blew the
11886 ms RPC budget, while the whole 698-test suite runs standalone in 4.9 s. Either the MTP
runner has large per-run overhead, or those mutants genuinely hang.

## Answer: the mutants hang

Same runner, same project, a slice whose mutants cannot hang — `ResilienceProperties.cs`, a plain
dictionary wrapper with no concurrency or timing primitives:

| slice | mutant | selected tests | elapsed |
| --- | --- | --- | --- |
| benign | 849 | 25 | **1 s** |
| benign | 853 | 20 | 0 s |
| benign | 859 | 12 | 0 s |
| ObjectPool | 1294 | 35 | **9 s** |
| ObjectPool | 1288 | 194 | 12 s (timed out) |
| ObjectPool | 1275 | 698 | 13 s (timed out) |

25 tests in 1 s against 35 tests in 9 s settles it. Per-run overhead does not scale that way, so
the ObjectPool timeouts are real: mutating the `Interlocked.CompareExchange` comparisons in a
lock-free pool livelocks, and those verdicts are legitimate kills.

Whole benign slice: 6 mutation runs in **1586 ms total**, no timeouts.

## But the phase breakdown moves the target

Benign run, total runner time 71.8 s:

| phase | cost | share |
| --- | --- | --- |
| Discovery | 1.81 s | 2.5 % |
| InitialTest — all 698 tests in **one** run | 6.74 s | 9.4 % |
| PerTestCoverage — 698 **separate** runs | **52.47 s** | **73.1 %** |
| Mutation — 6 runs | 1.59 s | 2.2 % |

```
per-test-coverage round trip : 75.17 ms x 698
amortised cost of a test     :  9.65 ms   (from the single 698-test run)
=> per-round-trip overhead   : 65.52 ms x 698 = 45.7 s
coverage phase vs its floor  : 7.79x
```

Per-test coverage costs nearly eight times what running the same tests once costs. That ~46 s is a
**fixed** cost of the run — it does not depend on how many mutants are being tested, so it is paid
in full whether the mutate scope is six mutants or the whole project.

## Cohort size buys most of it back

`--coverage-cohort-size` batches tests per round trip. Same benign slice:

| cohort | round trips | coverage phase | total wall | coverage speedup |
| --- | --- | --- | --- | --- |
| 1 (current default) | 698 | 52.47 s | 131 s | 1.00x |
| 8 | 88 | 18.67 s | 75 s | 2.81x |
| 32 | 22 | 11.58 s | 58 s | **4.53x** |

All three arms produce identical verdicts here (6 Killed, zero differences).

Per-round-trip overhead grows with the batch — 65.5 ms at cohort 1, ~136 ms at 8, ~220 ms at 32 —
because each request carries more test nodes and streams back more results. Total still falls
sharply, toward a floor of roughly the 6.7 s the tests themselves cost.

## What this does and does not settle

Settled:

- The MTP mutation phase is not slow. 2–25 tests complete in under a second.
- The ObjectPool RPC timeouts are genuine hangs, not runner artefacts.
- Per-test coverage is the dominant MTP cost on this project, ~46 s of it pure round-trip
  overhead, and cohort size removes most of it.

Not settled:

- Identical verdicts across cohort sizes were observed on **six benign mutants only**. It is not
  evidence that cohorts are verdict-safe in general — sharing coverage across a cohort makes
  attribution coarser, which is why the default was set to 1 in the first place. The earlier
  finding stands: cohort width amplifies contamination rather than causing it.
- The 131 s → 58 s total-wall figure is dominated by coverage because this slice has only six
  mutants. On a full run the mutation phase is far larger and the *proportional* saving smaller;
  the ~40 s absolute saving is what carries over.
- No MTP-versus-VsTest comparison on equal footing yet.

## Reproducing

```bash
cd test/Polly.Core.Tests
dotnet <fixed>/Stryker.CLI.dll --config-file ../../eng/stryker-config.json \
  --test-runner mtp --project Polly.Core.csproj --test-project Polly.Core.Tests.csproj \
  --mutate "**/ResilienceProperties.cs" --concurrency 1 \
  --coverage-cohort-size 32 --output out --reporter Json --skip-version-check
```

The phase numbers come from the `MTP phase performance:` lines; per-mutant timings come from
`--verbosity debug` (`Testing mutant(s) [id]` paired with `Test run for id:`).
