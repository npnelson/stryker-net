# Defect census: the MTP runner's coverage and identity channels

Nineteen defects in the channels between the MTP runner and the injected `MutantControl`, each with a
receipt. Companion to the RFC, which argues about structure; this document only establishes what is broken
and how you can see it.

**One of the nineteen is reached by an ordinary project on a released build** — that is
[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753), and it is the only one worth reading
this document for if you read nothing else. Five more are released but need an unlucky condition. The
remaining thirteen exist only on `feature/per-test-coverage-mtp`
([#3752](https://github.com/stryker-mutator/stryker-net/pull/3752)) and affect nobody today. The tables are
in that order — most likely to be met, first.

The fact behind most of this: **one test host loads one injected copy per mutated assembly**, each with its
own statics, and the runner hands them all the same coverage file and the same epoch relay.

**Reading the tables.** An id that is a **link** has a tracking item upstream; the rest are untracked. Each
receipt opens with how strongly the defect is established — **E2E** (visible in a full Stryker run on the
repo's own fixture), *unit* (a failing test you can run, §2), or *code* (read from the source and argued,
not demonstrated) — then the line-level citations. The *ordering* is my judgement; the **E2E** and *unit*
rows require taking my word for nothing.

Line references are `feature/per-test-coverage-mtp` @ `fbf2ed61` unless marked *(master)* = `ee06d3a1`.
**Every receipt was read in the checkout**, and every permalink was verified against the git object rather
than a rendered page.

---

## 1. The defects

### 1.1 Released — the code every `--test-runner mtp` user is running

Mechanisms re-checked present at master `7287834d`, not only at the `ee06d3a1` permalinks.

| id | fires when, and what it costs | receipt | upstream |
|---|---|---|---|
| [**C1**](https://github.com/stryker-mutator/stryker-net/issues/3753) | **A test project references two or more mutated projects** — the ordinary shape of a solution-mode run. Every copy in the host overwrites the same file with a whole-file write, so the last flush wins and the other assemblies' mutants are reported *No coverage* and never tested. Deterministic: the wrong answer is identical every run. | **E2E** + *unit* · [`275 WriteAllText`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L275) *(master [`245`](https://github.com/stryker-mutator/stryker-net/blob/ee06d3a10cb09a1364c468298dd226597cf1920d/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L245))* · one file for all copies [`174-181`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L174-L181) | **[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753)** — filed for this defect, with the full write-up. [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) fixed the cross-host form; the intra-host form remains |
| [**A1**](https://github.com/stryker-mutator/stryker-net/pull/3724) | **Two Stryker processes run at once on one machine.** Runner ids are `0..N-1` in every process, so `stryker-mutant-0.txt` collides on the first runner — the coverage file carries a pid and a nonce, the mutant-id file carries neither. On POSIX one process's `Dispose` then unlinks the path while another's warm hosts hold the old mapped inode; an orphaned mapping does not fault, so those hosts read a **frozen mutant id forever** and every remaining mutant is scored against the wrong mutation. | *code* · name [`79`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L79) · unlink [`1136-1141`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L1136-L1141) · mapped once [`176-210`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L176-L210) | **[#3724](https://github.com/stryker-mutator/stryker-net/pull/3724)** (open) fixes the naming half |
| **C6** | **A coverage producer dies** (host crash, timeout kill). There is no manifest of expected producers, and the force-kill path logs a warning that nothing connects to coverage — so a missing, killed or truncated producer is indistinguishable from "nothing covered", and its mutants are silently dropped rather than flagged. | *unit* · kill [`224-232`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/AssemblyTestServer.cs#L224-L232) · reader returns empty [`319-323`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L319-L323) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L694) → *a killed producer reads the same as one that covered nothing* | a deliberate policy choice in [#3696](https://github.com/stryker-mutator/stryker-net/pull/3696) |
| **A2** | **The mutant-id write throws** (temp unwritable, disk full, AV interference). Publication **fails open**: the write catches everything, logs a *warning*, and the run proceeds; the helper only updates `ActiveMutant` after a *successful* read, so the **previous** mutant stays active and is scored a second time under the new mutant's name. Silent false Survived/Killed. | *code* · [`153-158`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L153-L158) | untracked |
| **C4** | **A read overlaps a write** — needs an abnormal stop or a very late flush. Publication is neither atomic nor interlocked: on Unix a mid-write read can parse a truncated number into a **valid-looking wrong mutant id**; on Windows the sharing violation is caught and normalised to empty. | *code* · writer [`275`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L275) · reader [`319-323`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L319-L323) | untracked |
| **A4** | **The runner and the test host resolve different `Path.GetTempPath()`.** Env transport carries **bare filenames**, and each side resolves temp independently — the copy at whatever moment it lazily initializes. The child normally inherits the parent's environment, so this needs a deliberately divergent temp (container, `PrivateTmp`, scrubbed launch env). Uncommon in practice; listed for completeness, not as a live risk. | *unit* · runner [`174-181`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L174-L181) · copy [`73`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L73), [`88`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L88) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L643) → a bare `STRYKER_COVERAGE_FILE` | untracked — originates in [#3448](https://github.com/stryker-mutator/stryker-net/pull/3448) |

### 1.2 `feature/per-test-coverage-mtp` only — nothing released is affected

These cost users nothing today; they are what would ship with the feature.

| id | fires when, and what it costs | receipt | upstream |
|---|---|---|---|
| **E2** | **Any solution with two or more test projects**, on every capture pass after the first. Per-project capture **ignores its `project` argument** and replays the pool-wide catalog, so pass 2+ re-runs every assembly against epoch files a previous pass consumed — clipped or wrong coverage at `Normal`. | *unit* · signature [`190`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L190) vs [`204`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L204) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/MicrosoftTestPlatformRunnerPoolTests.cs#L410) → `capture for projectA also ran: projectB.dll, projectA.dll` | untracked — **the same scope bug was fixed in [#3516](https://github.com/stryker-mutator/stryker-net/pull/3516) and is reintroduced here** |
| **C2** | **A host holds two or more mutated assemblies** — the same precondition as C1, so equally ordinary. One scalar ack serves N copies: the first ack releases the read, so the runner cannot know whether every copy flushed, and `Dubious` never fires when it matters. | *code* · ack [`365`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L365) · wait [`408-443`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L408-L443). I have seen unscoped runs on `fbf2ed61` disagree with each other, which would be this, but the §2 reproduction is deterministic and does not establish it | untracked |
| **E6** | **A test executes no mutated code** — most tests, for most assemblies, in a multi-project solution. The poller only starts at the **first execution of instrumented code**, so such a test always burns the ack timeout, and its pending request can be consumed by a later test. Plausibly the branch's own unexplained "`perTestInIsolation` hangs sometimes" TODO. | *unit* · started from the type initializer [`85-90`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L85-L90) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.Core/Stryker.Core.UnitTest/InjectedHelpers/InjectedHelperTests.cs#L154), shared with E3 → *a mid-session copy's coverage is gone before the next read* | untracked |
| **A6** | **A runner is leased and returned across a mode change** — i.e. every per-test capture that uses the pool. Mode is enabled on `_allRunners` but disabled on `_availableRunners`, in **three mismatched pairs**, so a leased runner returns in the old mode. | *code* · [`124`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L124)/[`146`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L146) · [`194`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L194)/[`235`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L235) · [`253`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L253)/[`294`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L294) | raised in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752) |
| **E7** | **Always.** Per-test capture **discards the returned test updates** — only `timedOut` is kept — so `Normal`/`Exact` never establishes that the requested test ran, completed or passed. | *code* · [`479`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L479), [`557`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L557) | untracked |
| **A5** | **Always** — but it leaks temp files rather than corrupting a result. Cleanup is **dead code**: disabling the mode clears `_initializedPerTestFiles`, and the capture's `finally` always disables the mode, so `Dispose` later iterates an empty set and nothing ever deletes an epoch file. High frequency, low severity; it matters mostly because it guarantees the leftovers E1 then adopts. | *unit* · clear [`254`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L254) · `finally` [`233-239`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/MicrosoftTestPlatformRunnerPool.cs#L233-L239) · dispose loop [`1142-1146`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L1142-L1146) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L660) → *the epoch file must not outlive the runner that created it*. Also visible with no code at all: the §2 coverage-on run grows `/tmp/stryker-epoch-*` and `/tmp/stryker-coverage-pt-*` from 140 files to 156, and nothing ever removes them | raised in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752) review |
| **E4** | **Any ack timeout**, which E6 makes routine. It returns `Dubious` but — unlike the sibling failure paths at [`485`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L485) and [`522`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L522) — **does not discard the server**, so the stalled poller later fires mid-next-test. | *code* · [`500-508`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L500-L508) | untracked |
| **E5** | **A relay becomes unusable.** Relay death is sticky and nothing quarantines the host, so every remaining test pays the full ten-second timeout. Runtime, not correctness. | *code* · sticky flags [`46-47`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L46-L47) · repeated wait [`408-443`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L408-L443) | untracked |
| **E3** | **A host restarts mid-campaign** (crash or timeout). Every restart re-arms stale relay state: the new copy starts its cursor at zero against a possibly non-zero file, and may flush and reset during the next test. | *unit* · cursor [`57`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L57) · poller compare [`360`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L360) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.Core/Stryker.Core.UnitTest/InjectedHelpers/InjectedHelperTests.cs#L154), shared with E6 | untracked |
| **E1** | **A leftover epoch file is adopted** — which A5 guarantees will be lying around. An existing file is taken as-is, so a fresh counter can accept a stale ack immediately, or a new poller consume an old request. | *code* · [`369-374`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L369-L374) | raised in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752) |
| **C5** | **An isolated stop degrades to a kill.** Isolated "Exact" capture reuses one file across every test with no delete and no freshness token, then reads it after the stop — returning the **previous** test's coverage labelled `Exact`. Severe when it happens; needs the kill path. | *code*, and the weakest row here — no runnable receipt, and I have not reproduced it · [`568-573`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L568-L573) | untracked — an earlier variant was recognised in [#3516](https://github.com/stryker-mutator/stryker-net/pull/3516) |
| **C3** | **The coverage flush throws** — same rarity as A2. The flush swallows every exception to `Debug.WriteLine` (invisible in Release) and `ResetCoverage()` sits *after* the write inside the `try`, so a failed write leaves the previous epoch's file **and** an un-reset accumulator — and the epoch is acknowledged anyway, so it is credited at `Normal`. | *unit* · [`270-283`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L270-L283) then unconditional ack [`360-368`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.Core/Stryker.Core/InjectedHelpers/MutantControl.cs#L360-L368) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.Core/Stryker.Core.UnitTest/InjectedHelpers/InjectedHelperTests.cs#L238) → *the epoch is acknowledged although the flush threw* | untracked |
| **A3** | **After any crash.** Per-test names are runner id + assembly + `(uint)assembly.GetHashCode()`, carrying no pid and no nonce — so nothing in a leftover file identifies the process that wrote it, and **a crashed run's artifacts cannot be told apart from a live run's and are never reclaimed**. There is no reachable collision to worry about: .NET randomises string hashes per process. Cleanup, not correctness. | *unit* · [`259-266`](https://github.com/stryker-mutator/stryker-net/blob/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4/src/Stryker.TestRunner.MicrosoftTestPlatform/SingleMicrosoftTestPlatformRunner.cs#L259-L266) · [failing test](https://github.com/npnelson/stryker-net/blob/992278f71e549a9ced069e25a0245c0d09bd3e9b/src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest/SingleMicrosoftTestPlatformRunnerCoverageTests.cs#L624) → the name *must identify the owning process* | untracked — new naming in [#3752](https://github.com/stryker-mutator/stryker-net/pull/3752), not a change to anything existing; the aggregate coverage file does carry a pid and a nonce |

---

## 2. Running it yourself

### End to end, on the repo's own fixture — C1

The MTPSolution fixture already mutates two projects, but no sample test executes the second one, so no host
ever loads two copies — which is why CI has never seen this.
[`npnelson@82c4c52c`](https://github.com/npnelson/stryker-net/commit/82c4c52c) adds exactly that test, plus
the `ProjectReference` and a coverage-off config. No product changes, and it cherry-picks cleanly onto
master **and** onto `fbf2ed61`.

```bash
git clone https://github.com/stryker-mutator/stryker-net.git
cd stryker-net
git checkout fbf2ed61      # skip this line to run against master instead
git fetch https://github.com/npnelson/stryker-net repro/mtp-second-assembly && git cherry-pick FETCH_HEAD

# build the CLI once, quietly — otherwise every run reprints the repo's own build warnings
dotnet build src/Stryker.CLI/Stryker.CLI -c Release -v q -p:WarningLevel=0 -p:NoWarn=NU1608 -p:NuGetAudit=false
cd integrationtest/TargetProjects

# ground truth — coverage analysis off (~30 s)
dotnet run --no-build --project ../../src/Stryker.CLI/Stryker.CLI -c Release -- --config-file stryker-config-coverage-off.json --solution MicrosoftTestPlatform.slnx --test-runner mtp --mutate "**/RecursiveMath.cs"

# the bug — byte-for-byte identical but for the config, coverage analysis on (the default, ~1 min)
dotnet run --no-build --project ../../src/Stryker.CLI/Stryker.CLI -c Release -- --solution MicrosoftTestPlatform.slnx --test-runner mtp --mutate "**/RecursiveMath.cs"
```

Each run prints about 25 lines and no warnings. The added test asserts on the sequence `RecursiveMath`
prints, so its mutants are genuinely killable — which makes the contrast unambiguous rather than a matter
of interpretation:

| base | coverage analysis **off** | coverage analysis **on** (the default) |
|---|---|---|
| master `ee06d3a1` | 7 tested — 6 killed, 1 runtime error — score **100.00 %**, `# no cov` **0** | 0 tested — score **0.00 %**, `# no cov` **7** |
| `fbf2ed61` ([#3752](https://github.com/stryker-mutator/stryker-net/pull/3752)) | identical | identical |

Two identical runs on master and three on `fbf2ed61`, all the same: this is deterministic, not flaky. Seven
mutants that a test kills outright are reported as covered by nothing, and never run.

**A5 is visible from that same coverage-on run**, on `fbf2ed61` only. Count `/tmp/stryker-epoch-*` and
`/tmp/stryker-coverage-pt-*` before and after: on the run above it went from 140 to 156, and nothing ever
removes them.

### As unit tests — seven more defects

[`npnelson:test/mtp-channel-defects`](https://github.com/npnelson/stryker-net/tree/test/mtp-channel-defects)
sits on `fbf2ed61` and adds **only tests** — no production file is touched
([diff](https://github.com/stryker-mutator/stryker-net/compare/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4...npnelson:stryker-net:test/mtp-channel-defects)).

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

Each failure names its own test and prints the message quoted in the tables, and almost nothing else
appears. Everything else in both filtered suites passes (39 and 23), so the failures are the defects and
nothing else. The branch carries an eighth failing test, for **C1**, whose write-up is in
[#3753](https://github.com/stryker-mutator/stryker-net/issues/3753).

Three things in those commands are deliberate:

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
