# Fork status — unlanded MTP work

Fork-only document. Not intended for upstream; drop this commit when preparing
upstream PRs.

Baseline: upstream `stryker-mutator/stryker-net` master `b9e2559` (2026-08-10).
Every "still present upstream" claim below was checked by reading file content at
that commit, not inferred from commit messages.

## Why this exists

ORC and Beaq build Stryker from `fix/mtp-session-result-isolation` @ `b2fd3122`,
a branch based on a master from early July and **41 commits behind**. The fork
carries 24 MTP-related branches in varying states of staleness, several of which
turned out to be already landed upstream or to be upstream contributors' branches
rather than fork work. This is the audit.

## What this branch now carries

Five commits on top of `b9e2559`:

| Commit | What | Upstream status |
|---|---|---|
| `fix(mtp): run static mutants in a dedicated test-server process` | The isolation fix | Not upstream, nothing filed |
| `fix(mtp): merge coverage flushes from every mutated assembly in a host` | Within-host flush clobber | Not upstream |
| `test(mtp): cover a second mutated assembly in the MTP solution fixture` | Integration fixture for the above | — |
| `fix(core): honor target framework during initial build` | `--target-framework` ignored by initial build | Not upstream, **general bug** |
| `fix(mtp): isolate mutant-id control files per runner instance` | Control-file collision between concurrent runs | Not upstream |
| `fix(core): don't abort the run when executed tests are not enumerated` | Divide-by-zero aborts the run on one failing test | Not upstream, **general bug**, newly written here |

### The isolation fix: which commit, and why

The fix exists twice on the fork — `b2fd3122` (tip of `fix/mtp-session-result-isolation`)
and `9c7570c5` (ancestor of 11 branches, on a much newer base). Their **added lines are
byte-identical**; same patch, two bases.

`b2fd3122` is the right carrier, for a non-obvious reason: `9c7570c5`'s descendants
amended the isolation test double to override a **three-parameter**
`RunAssemblyTestsAsync(string, ITimeoutValueCalculator?, Func<TestNode,bool>?)`. That
third parameter comes from `7cb93394`, which is fork-only. Upstream's method is still
two-parameter, so the newer branches' test file **does not compile against upstream
master**. The "stale" branch is the one that targets today's upstream.

Rebasing it produced zero conflicts. Upstream's ~124 lines of drift in that file are
all per-assembly coverage-file plumbing — orthogonal to session/server lifecycle.

### Deviation from the source branch

`fix/mtp-coverage-flush-merge`'s `b964002f` wrapped the append in a 10-attempt
`IOException` retry. That retry is unreachable on current upstream: the only flush
trigger is `AppDomain.ProcessExit`, whose handlers run sequentially, and #3696 already
scoped the coverage file to one test-host process. The retry was needed for PR #3752's
per-test relay, which runs concurrent flush threads — a design not in upstream. Carried
the simpler form (matching `d8d37cdd` on `fix/mtp-ipc-file-hardening`) and corrected the
commit message accordingly.

### Verification

| Run | Scope | Result |
|---|---|---|
| `31344096797` | baseline `7287834`, before any change | success, 3/3 OS |
| `31345261667` | isolation fix alone | success, 3/3 OS |
| `31345268611` | isolation fix alone, integration matrix | success |
| `31345701229` | 5-commit stack | **failure** — see below |
| `31345702214` | 5-commit stack, integration matrix | success, **33/33 jobs**, incl. all 15 MTP/TUnit categories |
| `6a6df7fa` | full stack after the fix | success, 3/3 OS |

The one failure is worth recording, because neither source branch could have caught
it. The isolation fix's test double reconstructed the control-file path by hand from a
hardcoded runner id (`stryker-mutant-970.txt`) — a collision-avoidance hack, since a
shared id would have let concurrent tests stomp each other's file. The mutant-control-file
commit renames that file to carry a pid and nonce, so the test read a path that no longer
existed. Both commits pass on their own; they fail together. On the fork they live on
different lineages and never shared a base.

Fixed by reading `MutantFilePath` — the accessor that commit itself adds — instead of
rebuilding the name, which removes the hack rather than patching it: per-instance nonces
are what made a magic runner id unnecessary. Folded into that commit so every commit in
the series builds on its own.

## Already landed upstream — stop carrying these

| Branch | Landed as | Evidence |
|---|---|---|
| `fix/mtp-coverage-file-clobber` | `0f114515` — #3696, your own PR | Branch diff vs merge-base is **byte-identical** to the upstream commit's diff |
| `feature/test-reporting-mtp` | `5d497fa1` — #3541 | Upstream is a strict superset; `MtpTestCaseTests.cs` is an add/add conflict — same work arriving twice |
| `feature/mtp-performance` | `297d88cc` — #3650 (8 of 10 commits) | Squash body lists the eight subjects verbatim |

`feature/mtp-performance`'s filewatcher commit was **rejected in favour of memory-mapped
files**; upstream's `MutantControl` now carries a comment explaining why event-based
caching would race. Do not re-propose it.

## Not fork work

These are upstream contributors' branches the fork mirrors. Authorship checked with
`git log --format='%an %ae'`.

- `feature/mtp-performance`, `feature/mtp-performance-2` — Richard Werkman
- `feature/3656-auto-enable-mtp` — Werkman; stalled WIP carrying a `Stryker.Abstractions`
  public-API break (`init` → `set` plus a new interface member) that is vestigial — the
  loosened setter is never assigned
- `feature/per-test-coverage-mtp` — Werkman, PR #3752, unmerged

`feature/mtp-performance-2`'s bail feature is real and unlanded, and would *reduce*
isolation's cost rather than conflict with it — but it belongs upstream, not in this
fork's stack.

## Still real, not yet carried

Ranked by value, highest first.

1. **The RPC wire-format bug — now fully verified, and worse than triage suggested.**
   `RunTestsRequest` serializes the selection as `testCases`. Confirmed against Microsoft's
   own source (`microsoft/testfx`, `ServerMode/JsonRpc/JsonRpcMethods.cs`):

   ```csharp
   public const string Tests = "tests";
   ```

   No `"testCases"` constant exists anywhere in `ServerMode`, and the server reads the
   selection with `GetOptionalPropertyFromJson(properties, JsonRpcStrings.Tests)` — optional,
   so a misnamed selection yields null and **every test runs**, silently.

   This is not latent. `SingleMicrosoftTestPlatformRunner` line 668 builds
   `testsToRun` and passes it down through `AssemblyTestServer.RunTestsAsync` →
   `TestingPlatformClient.RunTestsAsync` → `new RunTestsRequest(RunId, TestCases: testNodes)`.
   A real selection is sent on every run and discarded every time. **That is the true cause
   of "MTP ignores test filters and runs every test in the assembly for every mutant"**,
   which ORC's mutation-scoring runbook records as a property of the platform.

   **Deliberately not carried.** The rename lives in `0162354e` (Werkman), bundled with
   per-test filter changes inside the unlanded #3752 stack — cherry-picking `1b4f582d` alone
   would not perform the rename, because its own pre-image already says `tests`. And
   `1b4f582d` is Amaury Levé's (Microsoft) follow-up marked `Fixes #3754`. This is upstream
   work in flight, not fork work. Carry `7cb93394` only alongside it, never before: an empty
   selection means nothing until selection works.

2. **`InitialisationProcess` divide-by-zero — FIXED on this branch.** See the commit
   `fix(core): don't abort the run when executed tests are not enumerated`.

   **The sibling occurrence at `MutationTestProcess.cs:189` is deliberately left alone**, and
   this is worth stating because it looks like the same bug and is not safe to "fix". There
   `testsCount` is the block-packing budget; at zero, `nextSet.Count + usedTests.Count > 0`
   trips immediately and every block holds exactly one mutant. That looks like a lost
   optimisation — but `TestMultipleMutantsAsync` sets
   `mutantId = mutants.Count == 1 ? mutants[0].Id : -1`, so a block with more than one mutant
   runs on MTP with **no mutation active at all** (the isolation fix ships a canary test,
   `TestMultipleMutantsAsync_RegularBatch_CurrentlyRunsUnmutated`, documenting exactly this).
   The zero budget is accidentally protecting MTP from silently unmutated runs. Restoring
   grouping would convert a wasted optimisation into wrong mutation results. Fix the
   single-id control channel first.

3. **`5ea2a74c` — `--additional-timeout` has no CLI flag.** `AdditionalTimeoutInput` is
   wired into config file reading/writing and has its own unit tests, but
   `CommandLineConfigReader` never registers it. One line plus a test. Verified absent
   upstream.

4. **`b18f5174` — timeout stage.** Replaces `bool TimedOut` with a
   `TestRunTimeoutStage?` enum distinguishing "the RPC never returned" from "the run
   exceeded its budget". Also swaps a `DateTime.UtcNow` delta for a `Stopwatch`. Trim the
   `LogDebug`→`LogWarning` promotions before offering it anywhere.

5. **Contracts #5 and #6 from `test/mtp-channel-defects`** — the only two pinned defects
   that are both live at upstream and pinned on no other branch:
   - `CoverageEnvironmentVariables_ShouldCarryFullPaths` — `STRYKER_COVERAGE_FILE` carries
     a bare filename that each injected copy recombines with its own `GetTempPath()`
   - `ReadCoverageData_ShouldDistinguishAMissingProducerFromOneThatCoveredNothing` — a
     crashed host and a host that covered nothing both read as empty

   The other six contracts on that branch pin defects in PR #3752 code that does not exist
   upstream; they will not compile against master. Park them with the feature.

6. **`docs/mtp-channel-rfc`** — an *orphan* branch (no merge base), not an abandoned one.
   The "337 behind" figure is an artifact of counting across unrelated histories. Seven
   files, all new paths under `docs/`, zero collision with upstream. Nothing but Renovate
   bumps has landed upstream since it was written, so its substance is current. **If you
   publish it, keep `test/mtp-channel-defects` alive at `36e02602`** — every "failing test"
   permalink in the census points there.

## Drop

- `claude/mtp-mutant-isolation-spike` — strict ancestor of two other branches, no unique content
- `claude/mtp-isolation-runnable` — `src/` tree is **identical** to
  `codex/mtp-static-state-regression`, but it trades a wired integration regression for an
  unwired probe project plus ~14,500 lines of spike CSVs
- `feature/per-test-coverage-mtp` — 56 behind, 7-hunk conflict against #3696, and missing
  four follow-up fixes the cohorts lineage already has
- `repro/mtp-second-assembly` — the bug it demonstrates is fixed by the flush-merge commit
  now on this branch. Salvage `stryker-config-coverage-off.json` (unique to it, useful for
  Beaq/ORC diagnostics); drop the fixture, or it collides with the one carried here and
  breaks the test-count baseline.

## Traps

- **`codex/mtp-coverage-cohorts` defaults `DefaultCoverageCohortSize` to 32**, which
  coarsens per-test coverage 32× — a coverage-accuracy regression enabled by default, in a
  path the code itself calls experimental. `codex/mtp-static-state-regression` carries
  `b4cb6514` defaulting it to 1; that branch's correction is not in the cohorts branch.
- **Phase-telemetry provenance.** The content of `codex/mtp-phase-utilization-telemetry`
  was absorbed verbatim (byte-identical blob) into `6b579908`, a commit whose subject is
  about cohort batching. If cohorts is ever dropped, that telemetry disappears inside a
  commit nobody would think to check.
- **Merging `experiment/beaq-mtp-candidate` or `codex/mtp-initial-build-target-framework`
  wholesale silently adopts PR #3752**, including a default-behaviour change making perTest
  the MTP default. Their clean `merge-tree` result is misleading. Harvest commits
  individually — which is what was done for `893d68c3`.
- **Flush-merge × per-test coverage is not additive.** Once flushes append, a per-test path
  must delete the coverage file before each epoch request or every test inherits its
  predecessors' coverage. The cohorts lineage does this; the standalone flush-merge branch
  does not, correctly, because it has no per-test path. Re-add it if per-test is ever ported.
- **`test/mtp-channel-defects` merges cleanly with the isolation fix and should not.** Both
  add a process-isolation path to the same class and both mutate `_assemblyServers` under
  `_serverLock`, with no defined interaction. Git reports success. Land isolation first,
  separately.

## Verification limits

No .NET SDK is available in the session container, and the SDK download hosts are blocked
by egress policy, so **nothing here was built or run locally**. Verification came from
GitHub Actions runs against this fork plus source reading at `refs/upstream/master`.

Specifically unverified: the mutation-score figures in the isolation commit message
(100% → 75%, 98.11% → 84.91%); every integration baseline number; and all performance
claims on the throughput branches — no benchmark artifact is committed anywhere in the
fork, those numbers live only in commit messages.

The one previously-unverifiable claim that *was* settled: MTP's wire name for the run
selection. Confirmed by reading `microsoft/testfx` directly rather than taking the commit
message's word for it.
