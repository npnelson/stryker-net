# Stryker's MTP runner against Polly on xunit v3 + Microsoft.Testing.Platform 2.x

Polly runs Stryker on VsTest today because xunit 2.9.3 has no MTP support. `npnelson/Polly`
branch `codex/xunit3-mtp2-benchmark` migrates it to `xunit.v3.mtp-v2` 3.2.2 on
`Microsoft.Testing.Platform` 2.3.3, branched from the same commit measured in `../RESULTS.md`
(`101d6af7`), touching 10 files.

The suite is healthy after the migration — `dotnet Polly.Core.Tests.dll` reports
**777 passed, 0 failed in 4.9 s**.

## What Stryker does with it

Released **dotnet-stryker 4.16.0**, `--test-runner mtp`, Polly's own config:

```
1480 mutants created, 358 to be tested
Coverage capture complete: 0 mutations covered, 0 static mutations
[ERR] It looks like the test coverage capture failed. Disable coverage based optimisation.
Stryker was unable to calculate a mutation score
```

Every one of the 358 mutants comes back `RuntimeError`. Nothing is scored.

| run | outcome | exit |
| --- | --- | --- |
| 4.16.0, `--test-runner mtp` | 358 RuntimeError, no score | **0** |
| 4.16.0, `--test-runner mtp --break-at 100` | 358 RuntimeError, no score | **0** |
| 4.16.0, `--test-runner mtp`, `coverage-analysis: off`, 2 mutants | 2 RuntimeError, no score | 0 |
| this spike branch, `--test-runner mtp` | 2113 test runs, 2112 failed, no score | 0 |

## The gate does not fire

Polly's `cake.cs` runs `dotnet stryker ... --break-at 100` and throws only when the exit code
is non-zero. It is zero here, because of `StrykerRunResult.cs:16` in 4.16.0:

```csharp
public bool ScoreIsLowerThanThresholdBreak()
{
    // If the mutation score is NaN we don't have a result yet
    return !double.IsNaN(MutationScore) && MutationScore < (double)Options.Thresholds.Break / 100;
}
```

A total runner failure produces `NaN`, the `!double.IsNaN` guard short-circuits, and the build
goes green on a run that measured nothing. The guard is presumably there for "no mutants to
test"; it does not distinguish that from "every mutant errored".

**This is independent of the cause below.** Whatever makes a run produce no score, the
threshold is silently skipped.

## The MTP 2.x protocol is not the problem

`rpcprobe.py` stands in for Stryker's end of the connection — it listens, launches the host the
way `DefaultTestServerConnectionFactory` does (`dotnet <assembly> --server --client-port N`),
and dumps every frame. Against MTP 2.3.3 all of it works:

- `initialize` → `{"serverInfo":{"name":"test-anywhere","version":"2.3.3"}}`
- `testing/discoverTests` → 698 test nodes
- `testing/runTests` → `{"result":{"attachments":[]}}`, 3 passed
- a **second** `testing/runTests` on the same host → also succeeds, so host reuse is fine
  despite `experimental_multiRequestSupport: false` in the advertised capabilities

Setting `STRYKER_MUTANT_FILE` / `STRYKER_COVERAGE_FILE` / `STRYKER_COVERAGE_EPOCH_FILE` on the
host changes nothing — it still discovers and runs.

Note when reading `rpc-handshake-and-run.out`: an earlier probe iteration sent a bare `{"uid": ...}`
and got `-32602 'display-name' field is missing`. That was the probe's bug, not Stryker's —
`Models/TestNode.cs` already sends `display-name`, `node-type` and `execution-state`.

## What actually kills the host

`host-crash.log` is the test host's own stdout/stderr, captured via `--log-to-file`:

```
System.IO.FileNotFoundException: Could not load file or assembly
  'Polly.Core, Version=8.0.0.0, Culture=neutral, PublicKeyToken=c8a3ffc3f8f825cc'.
  The system cannot find the file specified.
   at System.Reflection.RuntimeAssembly.GetExportedTypes()
   at Xunit.v3.XunitTestFrameworkDiscoverer.GetExportedTypes()
IsTerminating: True
```

It is raised on a thread-pool thread and is unhandled, so the whole process dies. Stryker sees
`ConnectionLostException: RemotePartyTerminated / Reached end of stream`, retries once, discards
the server, and records `RuntimeError`.

The message is literal. Stryker's MTP runner drives the test host **in place** in the test
project's output directory, and applies a mutation by moving the original aside:

```
$ ls artifacts/bin/Polly.Core.Tests/debug_net10.0/ | grep Polly.Core
Polly.Core.dll.stryker-unchanged        <- the original, renamed
                                        <- Polly.Core.dll is absent
```

Observed mid-run; `Polly.Core.dll` is restored when the run ends. xunit v3 discovery calls
`Assembly.GetExportedTypes()`, which forces every referenced assembly to load, so a host that
discovers during that window dies outright rather than reporting a failed test.

## Confirmed vs not

Confirmed:

- 358/358 `RuntimeError`, no score, exit 0 under `--break-at 100`, on released 4.16.0.
- The fatal error, its stack, and `IsTerminating: True`.
- `Polly.Core.dll` genuinely absent from the output directory during the run, with
  `Polly.Core.dll.stryker-unchanged` alongside.
- MTP 2.3.3 server mode, discovery, execution and host reuse all work when driven directly.
- The `NaN` short-circuit in `ScoreIsLowerThanThresholdBreak`.

Not established:

- Whether every failure is the swap race specifically, or whether the mutated assembly is
  sometimes written but unloadable (Polly is strong-named, and Stryker mutates the net8.0
  build of `Polly.Core` while the test project is net10.0).
- Whether this reproduces on xunit v3 projects generally or needs Polly's specifics.
- No timing comparison against the VsTest arms is possible while the MTP arm produces no
  verdicts, so the performance question in `../RESULTS.md` stays open.

## Reproducing

```bash
git clone https://github.com/npnelson/Polly && cd Polly
git checkout codex/xunit3-mtp2-benchmark
dotnet tool restore                       # pins dotnet-stryker 4.16.0
cd test/Polly.Core.Tests
dotnet stryker --project Polly.Core.csproj --test-project Polly.Core.Tests.csproj \
  --test-runner mtp --break-at 100 --config-file ../../eng/stryker-config.json
echo "exit code: $?"                      # 0
```

To watch the host die, add `--log-to-file` and read
`<output>/logs/test-servers/test-server-*.log`.

`rpcprobe.py <assembly.dll> <port>` reproduces the protocol exchange independently of Stryker.
