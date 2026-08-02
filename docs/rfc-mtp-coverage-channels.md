# RFC: coverage and identity channels for the MTP runner

**Status: exploration, not a proposal to adopt.** Several decisions in it are the maintainers' to make
(§4), and it asks for disagreement. It came from auditing the current channels, finding a defect family,
and then watching four successive replacement designs get refuted — §6 holds the refutations, which are
arguably more useful than the design.

---

## 1. Why this discussion exists

With the MTP runner and coverage analysis on (the default), a multi-project solution reports covered
mutants as *No coverage* and never tests them. On the repo's own MTPSolution fixture, with one added test
that executes the second mutated assembly, for `RecursiveMath`'s 7 non-ignored mutants — every one called
by name from that test:

| run, on master | verdicts |
|---|---|
| coverage analysis **off** — ground truth | all 7 tested: 6 survived + 1 runtime error |
| coverage analysis **on** — the default | **7 × No coverage** ("Not covered by any test"), two identical runs |

That these mutants are covered is a source-level fact, not a measurement dispute: coverage analysis is an
optimisation whose correctness condition is that it only skips mutants no test executes. Run-to-run
stability proves nothing here — on master the wrong answer is identical every time. On
`feature/per-test-coverage-mtp` the same commands additionally vary between runs: three runs, three verdict
sets, no warning. Commands and full numbers are in the linked issue.

No Stryker run needed to see it — a test-only branch
([diff](https://github.com/stryker-mutator/stryker-net/compare/fbf2ed618ff81c4643feb7a2d7263ba1127cb3e4...npnelson:stryker-net:test/mtp-channel-defects)):

```bash
git fetch https://github.com/npnelson/stryker-net test/mtp-channel-defects && git checkout FETCH_HEAD
dotnet test src/Stryker.TestRunner.MicrosoftTestPlatform.UnitTest              # 217 tests, 5 fail
dotnet test src/Stryker.Core/Stryker.Core.UnitTest --filter InjectedHelperTests #  26 tests, 3 fail
```

Eight failures, each naming a defect; everything else passes. Nineteen defects across three channels are
catalogued with per-line receipts in the companion census. They are not independent bugs — they share six
root causes, and two are *recurrences*: a naming scheme weaker than the one #3696 established, and a
per-project scoping bug #3516 had already fixed. That is the argument for discussing structure rather than
patching again. (#3706, open, is very likely this family in CI and probably closable.)

**None of this requires the redesign below.** Six of the nineteen are one-to-five-line fixes with failing
tests already written, and should land regardless.

---

## 2. What any replacement must satisfy

Each invariant carries the failure that produced it; §6 has the mechanism.

1. **One authority per control transition.**
2. **Many-writer evidence updates are monotonic, commutative, idempotent** — anything else needs
   arbitration, and arbitration is what keeps failing.
3. **Write authority comes from admission, and admission must be closeable** — enumerating registrations
   only observes producers that have already arrived; the one arriving after the scan is the one that
   corrupts the result.
4. **Completeness requires a seal over a producer domain, not a process exit** — a test's child process can
   inherit the session and outlive the host.
5. **Identity is fixed before launch and validated before authority is granted** — unit ranges can compile
   in; producer instances are dynamic and cannot.
6. **Capability belongs to artefacts; seals belong to producer domains** — one host loads several units, so
   per-artefact capabilities resolve to a conjunction over everything reachable in it.
7. **Empty evidence and unavailable evidence are different values** — today both read as "covered nothing"
   at full confidence.
8. **Attribution is bounded by the recipe that produced it**, and stays causally approximate regardless of
   protocol: escaped background work from one test can hit a probe in another's window; static
   initialisation and caching produce the inverse. A seal makes the *snapshot* sound, never the
   *attribution*.
9. **Infrastructure failure may only widen test selection** — never produce a new verdict.

Plus one instrumentation invariant: `MutationEffect(m) ⇒ ActivationProbe(m) executed first`. Without it,
absence of a baseline observation cannot argue that activating a mutant won't make its own site reachable.

---

## 3. The smallest production path

Excludes warm per-test capture, advisory lanes, and global probe planning — §6 says why each was dropped.

**Identity and storage.** One directory per host incarnation, passed as a single absolute path in one
environment variable. The parent creates and retains every mapping before launch and deletes nothing until
that incarnation is confirmed dead with all views closed. Identity lives in validated headers, not the
path. Each incarnation has **one immutable purpose** — changing purpose creates a new host, retiring
mode-flag desynchronisation as a category.

A directory also makes the mess reclaimable, which today it is not. Everything a run owns is one subtree:
normal cleanup is a single recursive delete, and a startup sweep reclaims subtrees whose owning session is
gone, so a crashed or killed run leaves nothing permanent behind. Compare the present state, where the
per-test artefacts leak on **every** run and are unreclaimable by construction — the names embed a
per-process-randomised hash, so no later run can even re-derive them to delete them. It also makes the
protocol legible: the live state of a host is a directory listing. (Where liveness is ambiguous the sweep
must prefer leaking a subtree over deleting a possibly-live one — a lazily-initialised helper cannot supply
a trustworthy startup lease.)

Storage is **one evidence map per instrumentation unit**, parent-created, each helper given a view bounded
to its own region. Transport identity is `(UnitId, LocalProbeId)`, ids assigned **unit-locally during that
unit's own discovery**, so parallel discovery and compilation are untouched and no solution-wide freeze is
needed. `CodeInjection` already rewrites helper source per unit, so the capability tuple compiles in and is
validated once against the map header before a writable view is granted — which makes stale artefacts
reliably detectable, including the case where a `bin` holds an instrumented assembly whose identity is
valid and only whose *vintage* is wrong.

**Closeable admission.** A producer creates a durable `pending` marker **before** obtaining any writable
view; the parent closes admission with one atomic `Directory.Move` of the admission directory. Closure and
write-authority denial in a single operation, no broker: a producer whose marker made it is in the closed
set and must reach a terminal state; one arriving afterwards finds no admission directory and fails closed.
Marker creation is the linearisation point; a marker whose producer then died is a timeout, hence
`Unknown`. Seal predicate:

```
admission closed && authenticated root exited && every admitted producer classified
&& no pending, foreign or faulted producer && no admitted producer retains write authority
```

The parent then copies the maps into an **immutable commit** — `Proved` must never reference live storage.

**Sealed replay groups.** Plan an immutable group: one test container, target and runtime, adapter
settings, artefact digest, and an *ordered* list of K tests. Launch one fresh host; run the K tests as K
sequential single-test requests, verifying each requested test's terminal update; probes store `1` into
monotonic `covered` / `static` / `outside-request` planes — no gate, reset, flush, acknowledgement or
read-modify-write anywhere; close admission and the domain; scan; commit. **The union is authoritative.**

Group by duration and topology, keeping framework lifecycle boundaries intact, with flaky, timeout-prone,
stateful and order-sensitive tests tending toward singletons. K=1 yields `FreshSingleTest` (not today's
`Exact`, and only with a verified terminal update). Failure marks the **whole group** `Unknown` and retries
in a fresh host; splitting is asymmetric — a child that kills may conclude `Killed`, all children surviving
does not prove the original would, and a split is a new recipe needing its own baseline.

**Activation without publication.** A fresh coverage host gets immutable `NoMutation`; a fresh mutation
host an immutable `(UnitId, LocalProbeId)` written before launch. The helper validates and caches once at
init, so the hot path is a local integer comparison — no mapped read, no tearing, no acknowledgement. A
mapping or validation failure must be a separately observable protocol fault followed by fail-stop
termination: returning `false` manufactures false survival; throwing can manufacture a false kill or be
swallowed by user code.

**Typed evidence.**

```
Proved(ReplayGroupId, Witness { …, OrderedExecutionWitness, SealKind, AdmissionClosure, ProtocolHealth },
                      Evidence { UnionCoverage, Integrity, Attribution, Origin })
| Unknown(FailureReason, PartialPositiveEvidence)
```

Zero ids mean nothing outside `Proved`. A failed test may yield complete evidence; a passing test may yield
none. `Attribution` is derived, never assigned. `NoCoverage` requires every eligible group to have complete
negative evidence.

**Keeping it that way.** None of the above survives contact with a fourth channel unless something
enforces it, and "developer discipline" is what produced the current state — three channels, three naming
schemes, one of them safe only by accident. Two mechanisms, both cheap:

*A Roslyn test over the injected source, asserting bans rather than an allowlist:* no `Path.GetTempPath`,
no `AppDomain`, no environment variable outside a declared set, no pointer access into mapped memory.
**The machinery already exists** — `InjectedHelperTests` parses and compiles that source at every
`LanguageVersion` — so this is the same trick the repository already uses to enforce the C# 2 ceiling,
pointed at identity and ownership instead of syntax. Adding a channel that invents its own name fails CI on
its first commit. Bans, not allowlists: an allowlist of permitted APIs churns on innocuous refactors, and a
test that goes red for renaming a variable gets deleted.

*Properties over the state machine*, which catch the failures a naming ban cannot: an accepted commit
implies matching identity and a valid seal; a faulted host is never reused; unknown evidence is never
converted to empty; a missing planned commit prevents reporting the campaign complete; and failure never
shrinks the selected test set.

---

## 4. Decisions that need maintainer input

These shape the design more than anything above.

1. **Are subprocess-hosted mutations expected to work?** If descendants are out of scope,
   `Proved(root-only)` is legitimate and admission closure suffices. If any spawned child must force
   `Unknown`, lazy registration cannot deliver that — it needs OS containment (a retained Job Object or
   cgroup) from launch.
2. **Can fresh mutation hosts be affordable?** The whole path assumes process-per-group is acceptable at
   some K.
3. **What test-order semantics does Stryker intend to preserve?** Grouping partitions the suite; which
   dependencies are contractual?
4. **Should an explicit `AllowUnsafeBlocks=false` constrain injected infrastructure?** Stryker controls the
   compilation and defaults it to true, so this is policy, not capability.
5. **Unit-local probe keys or global slots?** Unit-local avoids a solution-wide planning barrier; global
   ranges only pay off for warm capture.
6. **Which compatibility constraints are non-negotiable?** The C# 2 injected-source ceiling is honoured
   throughout here, but worth confirming it is still intended.

---

## 5. What would have to be proven

- **Kill-survival.** Green on Linux **and Windows** — a mapped write survives a force-kill with no flush,
  dispose or exit handler, and stays readable from another process. That was the likeliest place for this
  approach to fail, since Windows reaches the file through the cache manager and `TerminateProcess`
  differs from `SIGKILL`. macOS and ARM64 remain unrun.
- **Admission closure:** `Directory.Move` atomicity and the fail-closed path for a late producer, per
  platform.
- **Producer domain:** a fixture whose test spawns a child that loads an instrumented assembly and writes
  *after* the host exits — including the variant where the child waits for scan completion first.
- **Shadow comparison, done correctly:** compare selected plans always; use the conservative plan for the
  authoritative result; for sampled disagreements execute the legacy, sealed and coverage-disabled recipes
  **independently**. A full-suite run is a *reference policy*, not an intrinsic oracle.
- **Fuzz with a model oracle first:** pause the producer at each protocol step, kill it, assert the only
  legal outcomes are `Proved` or `Unknown`.

**Suggested first experiment**, which would settle more than further discussion: one fresh coverage host,
one per-unit monotonic map, immutable identity, typed `Proved`/`Unknown`, no lanes.

---

## 6. Considered and ruled out

**One writer per resource, with generation stamps.** Not a commit protocol: a copy can read the open epoch,
be descheduled before its first store, and write after the runner has scanned and returned `Proved(empty)`.
Moving the stamp after the write does not help — a copy can store A, stamp, then store B. **No injected
copy ever knows it has made its last write for a test.**

**Process exit as the seal.** Defeated by a child that inherits the session environment and outlives the
host. `Kill(entireProcessTree: true)` + `WaitForExitAsync` does not fix it; that API may return while
descendants are alive.

**Detecting foreign producers at seal time.** Misses the child that has not registered *yet*, then
registers and writes after the commit. "Known descendant set" is not a mechanism either — PIDs are reused
and descendants reparent.

**Treating `Kill()` survival as completeness.** A *completed* mapped store survives, which says nothing
about whether the test finished or every probe store happened. Positive evidence can survive a kill;
complete negative evidence cannot be manufactured by one.

**A managed-only writer gate.** Coupling an `Interlocked` counter to a mapped status byte leaves two
deterministic races: a later entrant proceeds while no `Busy` has been published, and a departing thread's
stale `Quiescent` overwrites a live thread's `Busy`.

**Read-back as activation publication.** Proves the parent can read its own bytes; nothing about whether a
helper mapped that incarnation, observed the value, or has a call still using the previous one.

**A conservative union run reconstructing counterfactual verdicts.** Fails on the first bail — the union
runs B then A, B kills, the run stops, A never executes, and the other channel's verdict is unknowable.
Even without bail, A-after-B need not behave like A alone.

**Grouping as strictly safer than per-test attribution.** It preserves the case where test A executes the
mutant and test B is the killing observer *only when they share a group*; split across groups the same
mechanism severs the dependency and reports a false `Survived`. Grouping is a semantic partition, not a
monotonic improvement.

**Global probe slot ranges.** Force a solution-wide discover→freeze→assign pipeline — the largest migration
cost in every draft — and are unnecessary once storage is per-unit, where `(UnitId, LocalProbeId)` is the
natural key.

**Measuring group affinity from the union.** Ten tests covering identical mutants and ten covering disjoint
portions produce the same union with opposite economics. Affinity needs advisory lanes or singleton
history, which is why lanes are deferred rather than foundational.

**Authoritative warm per-test capture.** Needs a writer gate whose admission lock cannot be amortised — a
producer has no notion of test boundaries, so enter/exit wraps every probe — and whose cross-process
ordering is unproven (`MemoryMappedViewAccessor` uses ordinary unmanaged reads and writes;
`Thread.MemoryBarrier` documents ordering for the current processor, not a cross-process happens-before).
Sealed replay groups make it optional, which is the most useful conclusion here: the feature this
investigation started from is probably unnecessary.

---

*Companion documents: the defect census with per-line receipts and upstream status; the kill-survival
experiment with its platform matrix.*
