# MTP static-state probe

A deterministic reproduction of the Microsoft Test Platform runner reporting wrong mutant verdicts
when the code under test writes process-global state during test-host startup
(stryker-mutator/stryker-net#3742).

## What it demonstrates

`MutantControl.IsActive` is a **runtime** switch. Any mutated code that executes **once per process**
escapes it: the first mutant to run on a test host bakes its effect in, and because the MTP runner
reuses that host for later mutant sessions, every subsequent mutant inherits it.

`Stryker.Core`'s existing carve-out (`IsStaticValue || MustBeTestedInIsolation`, which routes a mutant
to a dedicated host) does not cover this. The mutants here live in `StartupConfigurator`'s
constructor — ordinary instance code. It is the *caller* that runs once, from a `[ModuleInitializer]`
in the test assembly.

## Mechanism

`AppStartup.cs` holds process-global state and the ordinary instance code that writes it:

```csharp
public static class StartupState
{
    public static bool IsReady { get; private set; }
    internal static void SetReady(bool isReady) => IsReady = isReady;
}

public sealed class StartupConfigurator
{
    public StartupConfigurator(int seed) => StartupState.SetReady(seed > 0);
}
```

The test assembly runs it once, at host start, before the runner can change the active mutant:

```csharp
[ModuleInitializer]
internal static void Initialize() => _ = new StartupConfigurator(1);
```

`CandidateOperations.cs` holds twelve relational expressions. Each has a test that asserts
`StartupState.IsReady` and then calls the candidate **while ignoring its result**, so every candidate
mutant must survive. A candidate is reported killed only when its test inherited poisoned startup
state from an earlier mutant on the same host.

## Expected verdicts

28 mutants are created, 27 tested (one block-removal mutant is ignored).

| Mutants | Correct verdict | Why |
| --- | --- | --- |
| `SetReady` statement removed | Killed | leaves `IsReady` false |
| `seed > 0` → `seed < 0` | Killed | false for seed 1 |
| `seed > 0` → `seed >= 0` | **Survived** | provably equivalent for seed 1 |
| 24 candidate mutants | Survived | return values are never asserted |

So the correct result is **2 killed, 25 survived — a score of 7.41%**.

## Observed

Run from `StaticStateProbe.Tests/`, concurrency 1:

| Arm | Result | Score | Host starts |
| --- | --- | --- | --- |
| `--test-runner mtp` | 27 killed, 0 survived | **100.00%** | 3 |
| `--test-runner mtp --isolate-mutants` | 2 killed, 25 survived | **7.41%** | 29 |

The reuse arm's perfect score is entirely wrong. The clearest single case is `seed >= 0`: it is
mathematically equivalent to the original for seed 1, and no test can legitimately detect it.

## Note on mutant ordering

The *direction* of the error depends on which mutant is tested first, which follows mutant id, which
follows file order. `AppStartup.cs` is named to sort before `CandidateOperations.cs` so the startup
mutants run first and poison the host, producing false **Killed**.

Reverse that order and the same defect produces false **Survived** instead: the host starts with
startup code unmutated, and the startup mutants later cannot activate because the module initializer
has already run. Both directions were observed while building this fixture. Isolation produces the
correct 2/25 either way.

## Running it

```bash
dotnet build StaticStateProbe.Tests/StaticStateProbe.Tests.csproj -c Release   # 12/12 must pass clean
cd StaticStateProbe.Tests
dotnet <Stryker.CLI.dll> --test-runner mtp --concurrency 1                     # 100.00% - wrong
dotnet <Stryker.CLI.dll> --test-runner mtp --concurrency 1 --isolate-mutants   # 7.41%  - correct
```

Concurrency 1 is deliberate: it removes runner-assignment ambiguity and makes the contaminated-host
sequence deterministic.

This fixture is self-contained and does not reference `NetCore/TargetProject`, so it builds
independently of the rest of `integrationtest` (which currently fails to build on the .NET 10 SDK
with `CS0103: GeneratedNamespace` from its source generator).
