---
post_title: Deterministic experiment scheduling
author1: Stryker.NET maintainers
post_slug: deterministic-experiment-scheduling
microsoft_alias: stryker-net
featured_image: ""
categories: []
tags:
  - performance
  - benchmarking
ai_note: AI-assisted implementation for maintainer review.
summary: A versioned deterministic scheduler for paired performance experiments.
post_date: 2026-07-28
---
## Deterministic experiment scheduling

This component materializes a reproducible execution order for paired A/B
performance experiments. It is independent of Stryker production code and does
not know what treatments A and B represent.

The generator creates an equal number of `ABBA` and `BAAB` blocks, shuffles the
blocks from a signed seed, and returns the complete schedule before a benchmark
subject can run. The schedule carries
`stryker-abba-baab-xorshift64star/v1` as its generator version. Persist that
version, seed, block count, and every observation with future experiment
evidence. Validation proves that the stored order matches regeneration from the
declared seed and v1 algorithm; it does not prove seeds map to unique schedules
or guarantee that changing a seed changes a finite schedule.

Build and test from this directory:

```console
dotnet restore Stryker.Performance.ExperimentScheduling.slnx --locked-mode
dotnet build Stryker.Performance.ExperimentScheduling.slnx --configuration Release --no-restore
dotnet test --solution Stryker.Performance.ExperimentScheduling.slnx --configuration Release --no-build --no-restore
```

The directory-local `global.json` selects the Microsoft Testing Platform for
this test project without changing the rest of the repository.

## Contract

- Treatments are the generic labels `A` and `B`.
- Each block has four observations in either `ABBA` or `BAAB` order.
- A schedule has an even number of blocks and equal counts of both patterns.
- Each treatment appears equally often overall and at every block position.
- The generator version covers block construction, Fisher-Yates shuffling, and
  the xorshift64* pseudo-random number generator.
- The validator rejects altered metadata, coordinates, treatments, balance, or
  an order that differs from regeneration using the schedule's declared seed
  and generator version.

Golden vectors for positive, zero, and negative seeds freeze the complete v1
implementation. The v1 identifier permanently maps to this behavior. A future
algorithm requires a new version and explicit version dispatch; it must not
change v1 behavior or its golden vectors.

## Composition and non-goals

A later experiment runner should create and persist a validated schedule before
launching either treatment, then execute observations strictly in sequence.
Evidence collection can refer to the stable observation identifiers without
making this scheduler responsible for process or telemetry concerns.

This component does not:

- Launch processes or execute benchmark subjects.
- Define metric, evidence-bundle, or correctness-oracle schemas.
- Read clocks or assert anything about machine speed.
- Perform statistical analysis or make performance claims.
- Optimize Stryker production code.
