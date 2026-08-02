# Defect census: the MTP runner's coverage and identity channels

Nineteen defects across three channels, each with a receipt. Companion to the RFC, which argues about
structure; this document only establishes what is broken and how you can see it.

**One of the nineteen is reached by an ordinary project on a released build** — that is
[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753), and it is the only one worth reading
this document for if you read nothing else. Five more are released but need an unlucky condition. The
remaining thirteen exist only on `feature/per-test-coverage-mtp`
([#3752](https://github.com/stryker-mutator/stryker-net/pull/3752)) and affect nobody today. §2.5 ranks all
nineteen on that basis, once the ids below mean something.

**Defect families.** **A** — identity, environment transport, mode/cleanup bookkeeping. **C** — the
coverage channel (injected copies → runner). **E** — the per-test epoch relay. (**T** — test identity and
result semantics, and **L** — host lifecycle and transport, come from the same audit but are not
independently verified here; they are cited only where they bear on these three.)

**A defect id that is a link has a tracking item upstream**; the rest are untracked, and the *upstream*
column says what relationship each one has.

Line references are `feature/per-test-coverage-mtp` @ `fbf2ed61` unless marked *(master)* = `ee06d3a1`.
**Every receipt below was read in the checkout**, and every permalink was verified against the git object
rather than a rendered page.

---

## 1. The channels

| channel | direction | writers | identity in the name |
|---|---|---|---|
| mutant-id file | runner → copies | 1 | runner id **only** |
| coverage file | copies → runner | **N copies** | pid + runner id + nonce + assembly |
| per-test coverage | copies → runner | **N copies** | runner id + assembly + `GetHashCode()` |
| epoch relay (8 B) | runner ⇄ copies | **1 + N** | runner id + assembly + `GetHashCode()` |

One host loads **one injected copy per mutated assembly**, each with independent statics, and hands them
all the same coverage file and the same relay.

## 2. The defects

### 2.1 Identity, transport and bookkeeping (A)

| id | defect and consequence | receipt | upstream |
|---|---|---|---|
| [**A1**](https://github.com/stryker-mutator/stryker-net/pull/3724) | Mutant-id path is runner-scoped only, so independent Stryker processes steer each other. On POSIX one process's `Dispose` unlinks the path while another's warm hosts hold the old mapped inode — an orphaned mapping does not fault, so those hosts read a **frozen mutant id forever** and every remaining mutant is scored against the wrong mutation. | name [`79`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L79) · unlink [`1136-1141`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L1136-L1141) · map made once [`176-210`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L176-L210) | **[#3724](https://github.com/stryker-mutator/stryker-net/pull/3724)** (open) fixes the naming half |
| **A2** | Mutant-id publication **fails open**: the write catches everything, logs a *warning*, and the run proceeds; the helper only updates `ActiveMutant` after a *successful* read, so the **previous** mutant stays active. Silent false Survived/Killed. | [`153-158`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L153-L158) | untracked |
| **A3** | Per-test names omit pid and nonce, using runner id + assembly + `(uint)assembly.GetHashCode()`. Collision-free only because .NET randomizes string hashes per process — an undocumented detail — and that same accident makes crash leftovers unreclaimable. | [`259-266`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L259-L266) | untracked — a regression against the naming invariant [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) established |
| **A4** | Env transport carries **bare filenames**; runner and each copy independently resolve `Path.GetTempPath()`, at whatever moment that copy lazily initializes. | runner [`174-181`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L174-L181) · copy [`73`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L73), [`88`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L88) | untracked — originates in [#3448](https://github.com/stryker-mutator/stryker-net/pull/3448) |
| **A5** | Per-test cleanup is **dead code**: disabling the mode clears `_initializedPerTestFiles`, and the capture's `finally` always disables the mode, so `Dispose` later iterates an empty set. Nothing else ever deletes an epoch file. | clear [`254`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L254) · `finally` [`233-239`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L233-L239) · dispose loop [`1142-1146`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L1142-L1146) | raised in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752) review |
| **A6** | Mode is enabled on `_allRunners` but disabled on `_availableRunners` — **three mismatched pairs** — so a leased runner returns in the old mode. | [`124`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L124)/[`146`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L146) · [`194`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L194)/[`235`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L235) · [`253`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L253)/[`294`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L294) | raised in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752) |

### 2.2 Coverage channel (C)

| id | defect and consequence | receipt | upstream |
|---|---|---|---|
| [**C1**](https://github.com/stryker-mutator/stryker-net/issues/3753) | Every copy in a host overwrites the same file with a whole-file write, so the last flush wins and the other assemblies' mutants are reported *No coverage* and never tested. | [`275 WriteAllText`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L275) *(master [`:245`](https://github.com/stryker-mutator/stryker-net/blob/ee06d3a10cb09a1364c468298dd226597cf1920d/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L245))* · one file for all copies [`174-181`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L174-L181) | **[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753)** — filed for this defect; the reproduction, the numbers and the failing test are all there. [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) fixed the cross-host form; the intra-host form is what remains |
| **C2** | One scalar ack for N copies: the first ack releases the read, so the runner cannot know whether every copy flushed, and `Dubious` never fires when it matters. | ack [`365`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L365) · wait [`408-443`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L408-L443) | untracked |
| **C3** | The ack certifies an **attempt**: the flush swallows every exception to `Debug.WriteLine` (invisible in Release) and `ResetCoverage()` sits *after* the write inside the `try`, so a failed write leaves the previous epoch's file **and** an un-reset accumulator — credited at `Normal`. | [`270-283`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L270-L283) then unconditional ack [`360-368`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L360-L368) | untracked |
| **C4** | Publication is neither atomic nor interlocked. On Unix a mid-write read can parse a truncated number into a **valid-looking wrong mutant id**; on Windows the sharing violation is caught and normalised to empty. | writer [`275`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L275) · reader catch-all → empty [`319-323`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L319-L323) | untracked |
| **C5** | Isolated "Exact" capture reuses one file across every test with no delete and no freshness token, then reads it after a stop that can degrade to a kill — returning the **previous** test's coverage labelled `Exact`. | [`568-573`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L568-L573) | untracked — an earlier variant was recognised in [#3516](https://github.com/stryker-mutator/stryker-net/pull/3516) |
| **C6** | A missing, killed or truncated producer is indistinguishable from "nothing covered": there is no manifest of expected producers, and the force-kill path logs a warning that nothing connects to coverage. | kill [`224-232`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/AssemblyTestServer.cs#L224-L232) · reader returns empty [`319-323`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L319-L323) | a deliberate policy choice in [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) |

### 2.3 Epoch relay (E)

| id | defect and consequence | receipt | upstream |
|---|---|---|---|
| **E1** | An existing epoch file is adopted as-is, so a fresh counter can accept a stale ack immediately or a new poller can consume an old request. | [`369-374`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L369-L374) | raised in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752) |
| **E2** | Per-project capture **ignores its `project` argument** and replays the pool-wide catalog, so pass 2+ re-runs every assembly against epoch files a previous pass consumed — clipped or wrong coverage at `Normal`. | signature [`190`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L190) vs [`204 _testsByAssembly`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L204) | untracked — **the same scope bug was fixed in [#3516](https://github.com/stryker-mutator/stryker-net/pull/3516) and is reintroduced here** |
| **E3** | Every host restart re-arms stale relay state: the new copy starts its cursor at zero against a possibly non-zero file and may flush and reset during the next test. | cursor [`57`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L57) · poller compare [`360`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L360) | untracked |
| **E4** | Ack timeout returns `Dubious` but — unlike the sibling failure paths at [`485`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L485) and [`522`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L522) — **does not discard the server**, so the stalled poller later fires mid-next-test. | [`500-508`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L500-L508) | untracked |
| **E5** | Relay death is sticky: nothing quarantines a host whose relay is unusable, so every remaining test pays the full ten-second timeout. | sticky flags [`46-47`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L46-L47) · repeated wait [`408-443`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L408-L443) | untracked |
| **E6** | The poller only starts at the **first execution of instrumented code**, so a test touching no mutated code always burns the ack timeout, and its pending request can be consumed by a later test. | started from the type initializer [`85-90`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L85-L90) | untracked |
| **E7** | Per-test capture **discards the returned test updates** — only `timedOut` is kept — so `Normal`/`Exact` never proves the requested test ran, completed or passed. | [`479`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L479) and [`557`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L557) | untracked |

### 2.4 Executable receipts

Seven of these are demonstrated rather than argued.
[`npnelson:test/mtp-channel-defects`](https://github.com/npnelson/stryker-net/tree/test/mtp-channel-defects)
sits on `fbf2ed61` and adds **only tests** — no production file is touched
([diff](https://github.com/stryker-mutator/stryker-net/compare/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4...npnelson:stryker-net:test/mtp-channel-defects)).
From nothing:

```bash
git clone --branch test/mtp-channel-defects --single-branch --depth 1 https://github.com/npnelson/stryker-net.git mtp-defects

# MTP suite — 44 tests, 5 fail
cd mtp-defects/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest
dotnet build -v q -p:WarningLevel=0 -p:NoWarn=NU1608 -p:NuGetAudit=false
dotnet test --no-build --filter "FullyQualifiedName~SingleMicrosoftTestPlatformRunnerCoverageTests|FullyQualifiedName~MicrosoftTestPlatformRunnerPoolTests"

# injected-helper suite — 26 tests, 3 fail
cd ../Stryker.Core/Stryker.Core.UnitTest
dotnet build -v q -p:WarningLevel=0 -p:NoWarn=NU1608 -p:NuGetAudit=false
dotnet test --no-build --filter InjectedHelperTests
```

That prints the eight assertion messages and almost nothing else. Three things in it are deliberate:

- **Run from inside each project.** The MTP one requires it — its `global.json` selects the
  Microsoft.Testing.Platform runner and is resolved from the working directory, which is why
  `unit-test.yaml` sets `working-directory` for it. From the repo root, .NET 10 answers *"Testing with
  VSTest target is no longer supported"* and runs nothing.
- **Build quietly, then `--no-build`.** The flags mute pre-existing repo-wide restore and nullable
  warnings — roughly 400 lines of them — and nothing else. Drop them to see everything.
- **The filter.** `SingleMicrosoftTestPlatformRunnerTests.cs` puts a hard `[Timeout(1000)]` on 60 tests.
  That is on master and has nothing to do with this branch, but on a slower or loaded machine many of them
  time out, so an unfiltered run reports a different number every time. The two classes above hold all five
  MTP defect tests and carry no timeout attributes, so the count is the same everywhere.

| defect | test | what it prints |
|---|---|---|
| **E2** | [`CaptureCoverage_ShouldOnlyCaptureTestsOfTheGivenProject`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/MicrosoftTestPlatformRunnerPoolTests.cs#L410) | `capture for projectA also ran: projectB.dll, projectA.dll` |
| **C3** | [`MutantControl_ShouldNotAcknowledgeAnEpoch_WhenTheCoverageFlushFailed`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.Core/Stryker.Core.UnitTest/InjectedHelpers/InjectedHelperTests.cs#L238) | the epoch is acknowledged although the flush threw |
| **E3/E6** | [`MutantControl_ShouldKeepAMidSessionCopysCoverage_ForTheNextEpoch`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.Core/Stryker.Core.UnitTest/InjectedHelpers/InjectedHelperTests.cs#L154) | a mid-session copy's coverage is gone before the next read |
| **A5** | [`Dispose_ShouldDeletePerTestArtifacts_AfterPerTestModeIsDisabled`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L660) | `the epoch file must not outlive the runner that created it` |
| **A3** | [`PerTestArtifactNames_ShouldBeUniquePerProcessAndInstance`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L624) | `'stryker-coverage-pt-720-some-assembly-1980796130.txt' must identify the owning process` |
| **A4** | [`CoverageEnvironmentVariables_ShouldCarryFullPaths`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L643) | `STRYKER_COVERAGE_FILE was 'stryker-coverage-6006-721-…txt'` — a bare name |
| **C6** | [`ReadCoverageData_ShouldDistinguishAMissingProducerFromOneThatCoveredNothing`](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L694) | a killed producer reads the same as one that covered nothing |

That branch carries an eighth failing test, for **C1** — but C1's evidence belongs to
[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753), which has the failing test, the
solution-level reproduction on the repo's own MTP fixture and the run-to-run numbers. It is not repeated
here. Every other test in both filtered suites still passes (39 and 23), so the failures are the defects
and nothing else.

**C2** is the one other defect visible end to end: the same reproduction, cherry-picked onto `fbf2ed61`
instead of master, produces *three different verdict sets in three identical runs* with no warning — see
[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753). **C5** has no runnable receipt; it is
a code-reading claim, and I have not reproduced it.

**A5** is visible without any code at all: `ls /tmp/stryker-epoch-* /tmp/stryker-coverage-pt-* | wc -l`
before and after a `perTest` run — the count only grows.

### 2.5 What actually fires, and when

Now that the ids mean something, here they are ordered by how likely a user is to meet one. **Proof** is
how strongly each is established: *E2E* = visible in a full Stryker run on the repo's own fixture, *unit* =
a failing test in §2.4, *code* = read from the source and argued, not demonstrated. The ordering within
each group is my judgement from reading the code; the *E2E* and *unit* rows are the ones that do not
require taking my word for anything.

**Released — this is the code every `--test-runner mtp` user is running.** Mechanisms re-checked present at
master `7287834d`, not only at the `ee06d3a1` permalinks.

| # | id | fires when | proof |
|---|---|---|---|
| 1 | [**C1**](https://github.com/stryker-mutator/stryker-net/issues/3753) | A test project references **two or more mutated projects** — the ordinary shape of a solution-mode run. Deterministic: the second copy's flush erases the first, and those mutants are reported *No coverage* and never tested. | **E2E** + unit |
| 2 | [**A1**](https://github.com/stryker-mutator/stryker-net/pull/3724) | **Two Stryker processes run at once on one machine.** Runner ids are `0..N-1` in every process, so `stryker-mutant-0.txt` collides on the first runner — the coverage file carries a pid and a nonce, the mutant-id file carries neither. | code |
| 3 | **C6** | **A coverage producer dies** (host crash, timeout kill). Its mutants read as "covered nothing" and are silently dropped rather than flagged. | unit |
| 4 | **A2** | **The mutant-id write throws** (temp unwritable, disk full, AV interference). The run continues on the *previous* mutant, scoring it a second time under the new mutant's name. | code |
| 5 | **C4** | **A read overlaps a write** — needs an abnormal stop or a very late flush. A truncated number parses into a valid-looking wrong mutant id. | code |
| 6 | **A4** | **The runner and the test host resolve different `Path.GetTempPath()`.** The child normally inherits the parent's environment, so this needs a deliberately divergent temp (container, `PrivateTmp`, scrubbed launch env). Uncommon in practice — listed for completeness, not as a live risk. | unit |

**`feature/per-test-coverage-mtp` only — nothing released is affected.** These do not cost users anything
today; they are what would ship with the feature.

| # | id | fires when | proof |
|---|---|---|---|
| 7 | **E2** | **Any solution with two or more test projects**, on every capture pass after the first. Re-introduces the per-project scoping bug [#3516](https://github.com/stryker-mutator/stryker-net/pull/3516) already fixed once. | unit |
| 8 | **C2** | **A host holds two or more mutated assemblies** — the same precondition as C1, so equally ordinary. One ack releases the read for all N copies. | **E2E** |
| 9 | **E6** | **A test executes no mutated code** — most tests, for most assemblies, in a multi-project solution. Burns the full ack timeout each time, and leaves a request a later test can consume. Plausibly the branch's own unexplained "`perTestInIsolation` hangs sometimes" TODO. | unit |
| 10 | **A6** | **A runner is leased and returned across a mode change** — i.e. every per-test capture that uses the pool. | code |
| 11 | **E7** | **Always.** The requested test's result is discarded, so `Normal`/`Exact` never establishes that the test ran, finished or passed. | code |
| 12 | **A5** | **Always** — but it leaks temp files rather than corrupting a result. High frequency, low severity; it matters mostly because it guarantees the leftovers E1 then adopts. | unit |
| 13 | **E4** | **Any ack timeout**, which E6 makes routine. The stalled poller is not discarded and fires during a later test. | code |
| 14 | **E5** | **A relay becomes unusable**: nothing quarantines the host, so every remaining test pays the full ten seconds. Runtime, not correctness. | code |
| 15 | **E3** | **A host restarts mid-campaign** (crash or timeout). | unit |
| 16 | **E1** | **A leftover epoch file is adopted** — which A5 guarantees will be lying around. | code |
| 17 | **C5** | **An isolated stop degrades to a kill.** Returns the previous test's coverage labelled `Exact`. Severe when it happens; needs the kill path. | code |
| 18 | **C3** | **The coverage flush throws** — same rarity as A2. The epoch is acknowledged anyway. | unit |
| 19 | **A3** | **No in-process collision path exists** — .NET's per-process string-hash randomisation makes one effectively impossible. It matters as unreclaimable crash leftovers and as a cross-process collision, and as a regression against the naming invariant [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) established. Hardening, not a live bug. | unit |

---

## 3. Upstream issues that are symptoms of these defects

| upstream | state | relationship |
|---|---|---|
| **[#3706](https://github.com/stryker-mutator/stryker-net/issues/3706)** "MTPSolution integration test are flaky" (georgeglarson, 13 Jul 2026) | **open** | **This is C1 in the wild** — see [#3753](https://github.com/stryker-mutator/stryker-net/issues/3753). Ubuntu `survived 1 / timeout 0 / nocoverage 377` vs Windows+macOS `survived 2 / timeout 2 / nocoverage 374` — three mutants flipping. Attributed there to a System.IO.Abstractions bump ([#3704](https://github.com/stryker-mutator/stryker-net/pull/3704)). It is almost certainly not: those three are the `Timeout.cs` mutants only the MSTest project covers, and the post-fix baseline comment added by [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) names them exactly — *"Before coverage files were split per test host, the final flush overwrote the shared file, usually losing exactly those three mutants to NoCoverage"* (`ValidateStrykerResults.cs:204-208` on master). **Probably already fixed by [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) (merged 17 Jul, 4 days after the report) and simply never closed** — worth a maintainer verifying and closing. |
| **[#3696](https://github.com/stryker-mutator/stryker-net/pull/3696)** "fix(MTP): Flaky coverage results with MTP test runner" | merged 17 Jul 2026 | Fixed the **cross-host** instance of C1 (per-assembly coverage files + union at read). Its own description states the mechanism: *"each host replaces the previous contents instead of merging with them."* The **intra-host** instance (several injected copies inside one host) was not addressed — that is C1. |
| **[#3724](https://github.com/stryker-mutator/stryker-net/pull/3724)** "fix(mtp): isolate mutant-id control files per runner instance" | open | Fixes the naming half of A1, including the POSIX inode-freeze consequence. |
| **[#3752](https://github.com/stryker-mutator/stryker-net/pull/3752)** "feat(MTP): perTest and perTestInIsolation coverage" | open | Introduces E1–E7 and the per-test halves of A3, A5, A6, C2, C3. Its own TODO — *"Investigate issue with perTestInIsolation hanging indefinitely sometimes"* — is unexplained and plausibly E5/E6. |
| [#3689](https://github.com/stryker-mutator/stryker-net/issues/3689) "MTP per-test coverage analysis" | open | The feature request behind [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752); proposed `perTestInIsolation` as the MTP default for exactly these process-reuse risks. |
| [#3629](https://github.com/stryker-mutator/stryker-net/issues/3629) "coverage analysis assigns every covered mutant the full test suite" | closed (dup) | The aggregate-coverage limitation motivating per-test capture. |
| [#3628](https://github.com/stryker-mutator/stryker-net/issues/3628) "IsActive() stats the mutant file on every call" | closed | Why the mutant channel is memory-mapped — the constraint any redesign must preserve. |
| [#3727](https://github.com/stryker-mutator/stryker-net/issues/3727) "HashSet corruption under --concurrency > 1" | open | Not part of this census — a separate data race under `--concurrency > 1`, listed so it is not conflated with E-family symptoms. |
| [#3742](https://github.com/stryker-mutator/stryker-net/issues/3742), [#3692](https://github.com/stryker-mutator/stryker-net/issues/3692), [#3746](https://github.com/stryker-mutator/stryker-net/issues/3746), [#3551](https://github.com/stryker-mutator/stryker-net/issues/3551), [#3519](https://github.com/stryker-mutator/stryker-net/issues/3519) | mixed | Adjacent MTP lifecycle/timeout issues. No evidence linking them to this census; listed so nobody double-reports. |

**Nothing else in this census is documented upstream.** Searches run: coverage+MTP, "no coverage"/NoCoverage,
temp/leak/cleanup/concurrent, hang/Dubious/test host/restart. A2, A3, A4, C2, C3, C4, C5 and E3–E7 produced no matching issue.
