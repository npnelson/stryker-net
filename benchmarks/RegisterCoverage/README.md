## RegisterCoverage benchmark receipt

This out-of-tree BenchmarkDotNet harness measures the injected
`MutantControl` implementation from an explicitly selected Stryker.NET
worktree. It exists to produce reproducible evidence for
`fix/register-coverage-hotpath`; it is not part of that production change.

The benchmark compiles and executes the actual `MutantControl.cs` and
`MutantContext.cs` files from each source worktree. It does not reproduce the
registration algorithm in benchmark-only code, and reflection, source
selection, and input construction are outside the measured operation.

## Pinned comparison

- Baseline: `7287834d132a30ee5f6dba1d01d0d9853a0467f9`
- Candidate: `af8634a2ffeb39e2eb027ddaab78de8030a6e894`
- BenchmarkDotNet: `0.15.8`
- Target framework: `.NET 10`

The branch includes a
[preliminary ShortRun comparison](results/20260729T152206Z/comparison.md)
that validates the two-revision workflow and records both the speedup and the
allocation tradeoff. Run the full job on a quiet physical machine before
using numbers in the production pull request.

Use detached worktrees so later branch movement cannot change either input:

```bash
git worktree add --detach ../stryker-register-baseline \
  7287834d132a30ee5f6dba1d01d0d9853a0467f9
git worktree add --detach ../stryker-register-candidate \
  af8634a2ffeb39e2eb027ddaab78de8030a6e894
```

Run the full comparison from this benchmark branch:

```bash
./benchmarks/RegisterCoverage/run-comparison.sh \
  ../stryker-register-baseline \
  ../stryker-register-candidate
```

The runner rejects uncommitted harness changes and dirty source worktrees so
the recorded harness, baseline, and candidate SHAs describe the code that was
actually measured. Existing files under `results/` are excluded from the
harness cleanliness check.

Set `DOTNET_CMD` when `dotnet` is not on `PATH`:

```bash
DOTNET_CMD=/path/to/dotnet \
  ./benchmarks/RegisterCoverage/run-comparison.sh \
  ../stryker-register-baseline \
  ../stryker-register-candidate
```

For a shorter measured comparison before the full run:

```bash
./benchmarks/RegisterCoverage/run-comparison.sh \
  ../stryker-register-baseline \
  ../stryker-register-candidate \
  --job short
```

For a one-iteration smoke test that is not performance evidence:

```bash
./benchmarks/RegisterCoverage/run-comparison.sh \
  ../stryker-register-baseline \
  ../stryker-register-candidate \
  --job dry
```

Generated BenchmarkDotNet build artifacts and console logs are written under
the ignored `artifacts/` directory. GitHub Markdown and full JSON reports,
together with the source SHAs and `dotnet --info`, are copied to a
timestamped directory under `results/`.

## Workloads

The harness measures one complete coverage generation, ending with
`GetCoverageData()` so snapshot/reset costs are included:

- ascending unique IDs and repeated passes for 100, 1,000, and 10,000
  distinct mutants;
- one repeatedly hit mutant, covering the least-favorable case for the new
  membership index;
- normal registration followed by static-context promotion;
- 1, 4, and 8 parallel callers over repeated 1,000- and 10,000-mutant
  workloads.

Memory diagnostics are enabled for every workload. Threading diagnostics are
also enabled for the parallel workload to report `Monitor` contention.

## Interpretation

Compare the baseline and candidate reports from the same timestamped receipt.
The meaningful evidence is:

- scaling as distinct mutant count increases;
- behavior under repeated hits;
- the duplicate-only regression bound;
- allocated bytes per complete generation;
- parallel elapsed time and lock-contention counts.

Do not treat results from different machines as directly comparable. Run both
revisions while the same machine is otherwise idle, using the same power plan,
SDK, runtime, and benchmark arguments. BenchmarkDotNet results support the
hot-path claim; a separate end-to-end Stryker coverage run is still required
to quantify user-visible wall-clock impact.
