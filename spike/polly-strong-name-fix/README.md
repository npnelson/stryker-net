# Aligning the mutated assembly version with the built one

Fixes Stryker emitting the mutated assembly with a different version from the one the project
actually built, which makes strong-named projects unloadable in the test host.

## The defect

Stryker rebuilds the Roslyn compilation from the sources the analyzer reports. Those include the
SDK-generated `AssemblyInfo`, which can be stale — it carries whatever version was current when it
was last written, typically the `1.0.0.0` default. The mutated assembly is therefore emitted with
that version rather than the project's real one.

For `Polly.Core` the clean signed assembly is `Version=8.0.0.0`,
`PublicKeyToken=c8a3ffc3f8f825cc`. Stryker emitted the mutated one as `1.0.0.0`. The signed test
assembly binds to `Polly.Core, Version=8.0.0.0`, so the mutated assembly dropped in its place no
longer satisfies the reference and the MTP test host dies during discovery:

```
System.IO.FileNotFoundException: Could not load file or assembly
  'Polly.Core, Version=8.0.0.0, Culture=neutral, PublicKeyToken=c8a3ffc3f8f825cc'.
   at System.Reflection.RuntimeAssembly.GetExportedTypes()
   at Xunit.v3.XunitTestFrameworkDiscoverer.GetExportedTypes()
IsTerminating: True
```

xunit v3 discovery calls `Assembly.GetExportedTypes()`, which forces every reference to load, so
the failure takes down the whole host rather than failing a test.

### It is not an MTP defect — MTP only makes it fatal

The wrong version is emitted by `CsharpCompilingProcess`, which is runner-independent, so it
happens under VsTest too. Capturing the emitted assembly mid-run on Polly `101d6af7` (xunit 2.9.3,
VsTest, the configuration Polly ships today) against the same `ResilienceProperties.cs` slice:

| runner / framework | emitted mutated `Polly.Core.dll` | result |
| --- | --- | --- |
| VsTest, xunit v2, unfixed 4.16.0 | **`1.0.0.0`** / `c8a3ffc3f8f825cc` | 6 Killed — runs fine |
| MTP, xunit v3, unfixed 4.16.0 | **`1.0.0.0`** / `c8a3ffc3f8f825cc` | 9 RuntimeError — host dies |
| MTP, xunit v3, this branch | **`8.0.0.0`** / `c8a3ffc3f8f825cc` | 9 Killed — runs fine |

Polly's green mutation CI has been running against an assembly whose identity does not match the
one it built, for as long as it has been running. The VSTest test host resolves assemblies through
its own resolver, which binds by simple name from the test directory and tolerates the mismatch.
The default `AssemblyLoadContext` does not: an assembly whose version is *lower* than the reference
fails to bind, and the exception it raises is the famously misleading
`FileNotFoundException: ... The system cannot find the file specified` — which is about identity,
not about a missing file.

So this is a latent defect in Stryker's compiler that every strong-named project already carries,
masked by VsTest's permissive resolver and exposed the moment a project moves to MTP. As the
ecosystem moves that way — `global.json` `"test": { "runner": "Microsoft.Testing.Platform" }` — it
stops being masked.

## The fix

`src/Stryker.Core/Stryker.Core/Compiling/CsharpCompilingProcess.cs`, immediately before
`CSharpCompilation.Create` in `InitCSharpCompilation`. The assembly on disk is the authority, so
its version wins over whatever the reconstructed sources declare:

1. `analyzerResult.GetAssemblyPath()` gives the built DLL.
2. `System.Reflection.AssemblyName.GetAssemblyName(path).Version` reads its real version.
3. The syntax trees are materialized.
4. Assembly-targeted attributes named `AssemblyVersion` or `AssemblyVersionAttribute` are found.
5. The first argument of each is replaced with that version.
6. If none is declared, a tree holding
   `[assembly: global::System.Reflection.AssemblyVersionAttribute("<version>")]` is added.
7. The aligned trees go to `CSharpCompilation.Create`.
8. If the built assembly is missing or unreadable the trees are returned untouched, so projects
   that used to build still do.

Signing itself is not touched: the public key token was already correct before the fix, only the
version differed.

`GetSemanticModel` gained a fallback that matches a tree by `FilePath` when the exact instance is
not in the compilation, since rewriting the declaring tree replaces that instance while callers
still hold the original.

## Tests

`src/Stryker.Core/Stryker.Core.UnitTest/Compiling/CSharpCompilingProcessTests.cs` — each compiles
through `CsharpCompilingProcess` against a real on-disk assembly built at `8.0.0.0`, then reads the
emitted assembly version back:

| test | reconstructed source | emitted |
| --- | --- | --- |
| `ShouldRewriteStaleAssemblyVersionToTheBuiltOne` | declares `1.0.0.0` | `8.0.0.0` |
| `ShouldAddAssemblyVersionWhenSourceDeclaresNone` | declares nothing | `8.0.0.0` |
| `ShouldKeepDeclaredAssemblyVersionWhenBuiltAssemblyIsMissing` | declares `1.0.0.0`, no built assembly | `1.0.0.0` |

Full suite: 1544 passed, 14 skipped, 1 failed — `Authentication_Failure_Async`, which fails
identically on the unmodified base commit and needs network access.

## End-to-end verification

Polly `codex/xunit3-mtp2-benchmark` @ `7f2769026db59ea48f0bad3b6ebe25e89b268cd7`, run from
`test/Polly.Core.Tests`:

```bash
dotnet <fixed>/Stryker.CLI.dll \
  --config-file ../../eng/stryker-config.json --test-runner mtp \
  --project Polly.Core.csproj --test-project Polly.Core.Tests.csproj \
  --mutate "**/Utils/ObjectPool.cs" --concurrency 1 \
  --output StrykerOutput-strong-name-fix \
  --reporter ClearText --reporter Json --verbosity debug --log-to-file --skip-version-check
```

| check | result |
| --- | --- |
| `IsSolutionContext` | `false` |
| test projects selected | `Analyzing 1 test project(s).` |
| MTP discovery | `Discovered 698 tests` |
| TFM selection | `Selected version is net8.0` for `Polly.Core`; test project on `net10.0` |
| strong-name / `FileNotFoundException` failures | 0 in the Stryker log, 0 across all `logs/test-servers/*` |
| emitted mutated `Polly.Core.dll` | `8.0.0.0` / `c8a3ffc3f8f825cc` |
| restored `Polly.Core.dll` | `8.0.0.0` / `c8a3ffc3f8f825cc` |
| leftover test-host processes | none |

Same slice, same clone, before and after:

```
unfixed 4.16.0 : {'Ignored': 302, 'CompileError': 10, 'RuntimeError': 9}   no score
this branch    : {'Ignored': 302, 'CompileError': 10, 'Killed':       9}   score computed
```

The MTP runner reaches discovery, per-test coverage (698 leases) and mutation (9 leases), and
`708 test runs (700 completed, 0 failed, 8 RPC timeouts)`. Before the fix the same run reported
every test run failed.

### The reported score is not a baseline

8 of the 9 mutants hit the MTP RPC timeout (at the 11886 ms budget this run computed) and were
recorded as detected anyway, so the reported 100.00 % says nothing about Polly's real mutation
score. This run proves only that the signed assembly loads and that MTP reaches discovery,
coverage and mutation execution. The timeout behaviour is a separate defect — see
`../polly-determinism/RESULTS.md`.

## Environment notes

Two accommodations, neither affecting the result:

- Polly's `global.json` pins SDK `10.0.302`; this box has `10.0.110`, so it was repointed to
  `10.0.100` / `latestPatch`. No other Polly file was modified.
- The VsTest comparison arm ran on a separate clone of Polly at `101d6af7` (xunit 2.9.3), the
  commit the determinism baselines in `../polly-determinism/RESULTS.md` were measured on.
- The net8.0/net9.0 `Microsoft.NETCore.App.Host.linux-x64` packs would not restore from inside
  Polly, whose `NuGet.config` uses `<clear />` plus package source mapping, so `OutputType=Exe`
  failed those TFMs with MSB3030. Building a throwaway net8.0 and net9.0 console app outside the
  repo primes the global package cache, after which Polly builds all three TFMs unmodified.
