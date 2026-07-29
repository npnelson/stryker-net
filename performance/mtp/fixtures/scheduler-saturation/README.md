---
post_title: Scheduler saturation fixture
author1: Stryker.NET maintainers
post_slug: scheduler-saturation-fixture
microsoft_alias: stryker-net
featured_image: ""
categories: []
tags:
  - performance
  - mutation-testing
ai_note: AI-assisted implementation for maintainer review.
summary: A deterministic fixture for characterizing Stryker mutation-group concurrency.
post_date: 2026-07-28
---

## Scheduler saturation fixture

This fixture characterizes Stryker's outer mutation-group scheduler. It has
320 independent numeric assignment mutation sites and one shared semantic
oracle. Each framework adapter exposes exactly one sequential logical test
that executes every site.

The fixture compares complete xUnit-v3/MTP and TUnit/MTP stacks. Their locked
transitive package graphs differ, so results must not be described as an
isolated comparison of test-framework implementation cost.

## Fixture contract

- The target and semantic oracle are shared by both framework adapters.
- The source contains 320 explicit assignment sites; it does not generate them
  at build or run time.
- xUnit and TUnit test scheduling are disabled at assembly level.
- The package graphs are committed as lock files and restored in locked mode.
- Stryker uses MTP, Standard mutations, disabled coverage analysis, disabled
  bail, and disabled mutant mixing.
- Stryker concurrency remains a run argument and is never baked into the
  fixture.
- TUnit's HTML, JSON, and GitHub reporters are disabled for validation and
  mutation runs.

`Directory.Build.props` maps source paths to a stable logical root for
deterministic compilation. The validator reports both normalized semantic
input hashes and the raw target binary hash. The raw hash identifies only the
current build artifact; it is not a substitute for semantic identity across
machines or SDK installations.

## Validate preflight and smoke behavior

Run the cross-platform PowerShell validator from the repository root:

```powershell
pwsh ./performance/mtp/fixtures/scheduler-saturation/validate.ps1
```

This checks the complete input inventory, locked dependency versions, static
site sequence, manifest/config agreement, deterministic checksum, Release
builds, discovery, and one passing smoke test per framework. Its output says
`mutationProof.validated: false` because source inspection and direct test
execution do not prove the Stryker MTP `--server` path.

## Produce the mutation proof

Build the repository CLI, then pass its absolute path to the fixture wrapper:

```powershell
dotnet build ./src/Stryker.CLI/Stryker.CLI/Stryker.CLI.csproj --configuration Release

pwsh ./performance/mtp/fixtures/scheduler-saturation/run-stryker.ps1 `
  -StrykerCliPath ./src/Stryker.CLI/Stryker.CLI/bin/Release/Stryker.CLI.dll `
  -Concurrency 4
```

The wrapper runs both framework projects with the same concurrency, writes to
a new output directory, and invokes the validator with the two JSON reports.
The validator requires each report to contain exactly 321 mutants: 320 killed
assignment mutations and one ignored block-removal mutation. It also binds
the reports to the embedded target and test sources, exact framework test
identity, locations, replacements, statuses, and `killedBy` cardinality. It
then compares a canonical mutation fingerprint that abstracts only the
framework-specific test identity.

xUnit's test ID is a 64-character lowercase hexadecimal value that can vary
with the build path or platform. The validator requires that shape, the exact
test name, and exact referential agreement between the reported test ID and
every `killedBy` entry; it does not mistake a machine-specific ID for semantic
identity.

Existing reports can be checked directly:

```powershell
pwsh ./performance/mtp/fixtures/scheduler-saturation/validate.ps1 `
  -XUnitStrykerReportPath ./xunit/reports/mutation-report.json `
  -TUnitStrykerReportPath ./tunit/reports/mutation-report.json
```

Both report paths are required together. A report from an unrelated target or
test adapter is rejected even if its aggregate mutation counts match.

## Interpretation boundary

This fixture supports controlled experiments; it does not establish a timing
conclusion. Measurements include Stryker, MTP, framework integration, process
startup, host lifecycle, and the separately locked dependency graph. Do not
add elapsed-time thresholds to CI. Use randomized, balanced experiment
scheduling and retain the validator output alongside every measured run.
