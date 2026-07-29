---
post_title: MTP microbenchmark foundation
author1: Stryker.NET maintainers
post_slug: mtp-microbenchmark-foundation
microsoft_alias: stryker-net
featured_image: ""
categories: []
tags:
  - performance
  - benchmarking
  - mutation-testing
ai_note: AI-assisted implementation for maintainer review.
summary: >-
  A deterministic BenchmarkDotNet foundation for investigating Stryker MTP
  performance.
post_date: 2026-07-28
---

## MTP microbenchmark foundation

This project is the deterministic BenchmarkDotNet foundation for investigating
Microsoft Testing Platform (MTP) performance. It does not yet claim to measure a
production hot path. The sole discoverable benchmark is an infrastructure smoke
check that proves benchmark discovery, out-of-process execution, and report
generation work on Windows and Linux.

BenchmarkDotNet `0.15.8` is pinned directly and by `packages.lock.json`.
The version was verified against the [official NuGet listing][bdn-nuget]. The
configuration uses the default out-of-process toolchain for .NET 10, the memory
diagnoser, and full JSON, CSV, and GitHub Markdown exporters.

## Workload contract

The generator creates six immutable profiles: xUnit and TUnit each have
all-pass, one-failure, and partial-run variants. Each profile owns the same
framework-specific 128-test catalog and a deterministic sequence of terminal
updates.

| Shape | Passed | Failed | Not run | Total discovered |
| --- | ---: | ---: | ---: | ---: |
| All pass | 128 | 0 | 0 | 128 |
| One failure | 127 | 1 | 0 | 128 |
| Partial run | 15 | 1 | 112 | 128 |

The xUnit catalog contains 128 lowercase hexadecimal IDs that are exactly 64
characters. The TUnit catalog contains 128 IDs from 133 through 164 characters,
with a mean of `147.3125`: one at 133, 89 at 147, 37 at 148, and one at 164.

The partial-run profile emits 15 passing updates followed by its first
conclusive failure as the sixteenth update, then stops with 112 tests unrun.
This models an early-termination path rather than arbitrary truncation of an
all-pass run.

The validation command checks counts, ID uniqueness and distributions, state
totals, cross-profile catalog equality, repeatability, six profile SHA-256
golden hashes, and one whole-contract golden hash. Fixed tokens and line feeds
make the canonical hashes independent of operating system and current culture.

## Build and validate

Run these commands from the repository root on either Windows or Linux:

```shell
cd performance/mtp/microbenchmarks
dotnet restore --locked-mode
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --validate-workloads
dotnet run --configuration Release --no-build -- --list flat
```

The validator is suitable for normal CI. Normal CI should build, validate the
contract, and list benchmark discovery; it must not fail on elapsed-time or
allocation thresholds. Stable performance comparisons belong in controlled,
repeatable benchmark jobs with retained artifacts.

Exit behavior is part of the harness contract. Invalid BenchmarkDotNet
arguments, critical validation errors, and failed benchmark reports return `1`.
Successful validation and harness runs, plus list, info, help, and version
commands, return `0`.

The focused verifier launches all four representative cases as subprocesses. It
includes the Dry harness run but makes no timing assertion:

```shell
pwsh ./verify-exit-codes.ps1
```


To exercise the complete BenchmarkDotNet launch/export path without treating the
number as a product measurement, run:

```shell
dotnet run --configuration Release --no-build -- --verify-harness
```

BenchmarkDotNet writes its JSON, CSV, and GitHub Markdown output below
`BenchmarkDotNet.Artifacts/` by default.

## Planned benchmarks

The foundation deliberately postpones production-facing benchmarks until each
benchmark has a named question, isolated operation, and representative input:

- B1: MTP request construction and serialization for deterministic test IDs.
- B2: test-node update ingestion and terminal-state aggregation.
- B3: scheduler completion accounting for full and partial runs.

When a production hot path is selected, add a direct project reference and the
narrowest visibility bridge required by that benchmark. Do not copy production
logic into this project or optimize production code from the smoke benchmark.

[bdn-nuget]: https://www.nuget.org/packages/BenchmarkDotNet/0.15.8
