## RegisterCoverage comparison

This is a preliminary `ShortRun` receipt: one launch, three warmups, and three
measured iterations per case. It ran on a shared 9-core Intel Xeon Platinum
8370C environment where BenchmarkDotNet could not raise process priority.
Use it as directional evidence and as validation of the harness, not as the
final headline measurement for a pull request.

- Harness: `b6d3e46835518edc94235ca92d5df8d193a4013c`
- Baseline: `7287834d132a30ee5f6dba1d01d0d9853a0467f9`
- Candidate: `af8634a2ffeb39e2eb027ddaab78de8030a6e894`
- Runtime: .NET 10.0.10

Speedup is baseline mean divided by candidate mean, so values above 1 mean
the candidate was faster.

| Workload | Distinct/hits | Passes/workers | Baseline mean | Candidate mean | Speedup |
| --- | ---: | ---: | ---: | ---: | ---: |
| Sequential | 100 | 1 pass | 3.8 us | 5.4 us | 0.71x |
| Sequential | 100 | 10 passes | 34.4 us | 39.8 us | 0.87x |
| Sequential | 1,000 | 1 pass | 58.9 us | 50.3 us | 1.17x |
| Sequential | 1,000 | 10 passes | 521.6 us | 370.1 us | 1.41x |
| Sequential | 10,000 | 1 pass | 3,139.7 us | 1,343.5 us | 2.34x |
| Sequential | 10,000 | 10 passes | 30,851.1 us | 4,548.3 us | 6.78x |
| Normal then static | 100 | - | 7.6 us | 11.0 us | 0.69x |
| Normal then static | 1,000 | - | 140.2 us | 104.6 us | 1.34x |
| Normal then static | 10,000 | - | 12,021.9 us | 1,890.9 us | 6.36x |
| Concurrent | 1,000 | 1 worker | 570.3 us | 389.0 us | 1.47x |
| Concurrent | 1,000 | 4 workers | 2,045.6 us | 2,020.8 us | 1.01x |
| Concurrent | 1,000 | 8 workers | 3,598.9 us | 2,524.3 us | 1.43x |
| Concurrent | 10,000 | 1 worker | 33,188.9 us | 4,501.3 us | 7.37x |
| Concurrent | 10,000 | 4 workers | 51,180.3 us | 22,321.3 us | 2.29x |
| Concurrent | 10,000 | 8 workers | 63,654.8 us | 21,779.0 us | 2.92x |
| One repeated ID | 1,000 hits | - | 37.4 us | 34.9 us | 1.07x |
| One repeated ID | 10,000 hits | - | 352.1 us | 349.8 us | 1.01x |
| One repeated ID | 100,000 hits | - | 4,347.6 us | 4,353.4 us | 1.00x |

### Allocation tradeoff

The candidate trades more per-generation allocation for faster membership
checks. Pass count does not change these figures because each measured
operation takes one snapshot and reset.

| Workload | Distinct/hits | Baseline allocated | Candidate allocated | Candidate/baseline |
| --- | ---: | ---: | ---: | ---: |
| Sequential | 100 | 1,256 B | 7,320 B | 5.83x |
| Sequential | 1,000 | 8,496 B | 67,224 B | 7.91x |
| Sequential | 10,000 | 131,472 B | 670,192 B | 5.10x |
| Normal then static | 100 | 2,408 B | 14,408 B | 5.98x |
| Normal then static | 1,000 | 16,888 B | 134,216 B | 7.95x |
| Normal then static | 10,000 | 262,840 B | 1,340,151 B | 5.10x |
| Concurrent, 8 workers | 10,000 | 134,520 B | 673,218 B | 5.00x |
| One repeated ID | any hit count | 144 B | 376 B | 2.61x |

At 10,000 distinct IDs, the candidate adds about 526 KiB for a normal
generation and about 1.03 MiB for a normal-plus-static generation. The
duplicate-only case adds 232 bytes and showed no meaningful elapsed-time
regression in this run.

### Lock contention

The threading diagnoser reported lower contention in the two 10,000-ID
multi-worker cases. The 1,000-ID results were mixed.

| Distinct IDs | Workers | Baseline contentions | Candidate contentions |
| ---: | ---: | ---: | ---: |
| 1,000 | 4 | 6.29 | 4.63 |
| 1,000 | 8 | 11.20 | 11.38 |
| 10,000 | 4 | 313.50 | 111.63 |
| 10,000 | 8 | 188.63 | 69.44 |

### What this establishes

The result has the expected algorithmic crossover: the candidate is slower
for 100 distinct IDs, roughly even to moderately faster around 1,000, and
substantially faster at 10,000. The repeated-single-ID control remains flat,
which argues against a broad regression in the common duplicate path.

It also exposes a real cost that should be disclosed in review: keeping the
ordered lists while adding membership sets increases allocation by roughly
five to eight times for distinct-ID generations. A full BenchmarkDotNet run
on a quiet physical machine, followed by an end-to-end Stryker coverage run,
is still needed before making a user-visible wall-clock claim.
