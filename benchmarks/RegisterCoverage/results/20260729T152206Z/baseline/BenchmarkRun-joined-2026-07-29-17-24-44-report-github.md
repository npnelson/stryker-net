```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 2.79GHz), 1 CPU, 9 logical and 9 physical cores
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  Toolchain=.NET 10.0  IterationCount=3
LaunchCount=1  WarmupCount=3

```
| Type                                      | Method                          | DistinctMutants | Passes | WorkerCount | HitCount | Mean          | Error         | StdDev        | Gen0   | Completed Work Items | Lock Contentions | Allocated |
|------------------------------------------ |-------------------------------- |---------------- |------- |------------ |--------- |--------------:|--------------:|--------------:|-------:|---------------------:|-----------------:|----------:|
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **100**             | **1**      | **?**           | **?**        |      **3.817 μs** |      **2.491 μs** |     **0.1365 μs** | **0.0458** |                   **NA** |               **NA** |    **1256 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **100**             | **10**     | **?**           | **?**        |     **34.413 μs** |     **31.633 μs** |     **1.7339 μs** |      **-** |                   **NA** |               **NA** |    **1256 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **100**             | **?**      | **?**           | **?**        |      **7.571 μs** |      **2.968 μs** |     **0.1627 μs** | **0.0916** |                   **NA** |               **NA** |    **2408 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **1000**            | **1**      | **?**           | **?**        |     **58.905 μs** |     **29.180 μs** |     **1.5994 μs** | **0.3052** |                   **NA** |               **NA** |    **8496 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **1**           | **?**        |    **570.344 μs** |    **265.737 μs** |    **14.5660 μs** |      **-** |                    **-** |                **-** |    **9960 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **4**           | **?**        |  **2,045.620 μs** |  **2,139.076 μs** |   **117.2500 μs** |      **-** |               **2.9922** |           **6.2891** |   **10563 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **8**           | **?**        |  **3,598.933 μs** |  **4,507.602 μs** |   **247.0769 μs** |      **-** |               **6.3516** |          **11.2031** |   **11382 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **1000**            | **10**     | **?**           | **?**        |    **521.569 μs** |    **134.701 μs** |     **7.3834 μs** |      **-** |                   **NA** |               **NA** |    **8496 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **1000**            | **?**      | **?**           | **?**        |    **140.213 μs** |    **110.086 μs** |     **6.0342 μs** | **0.4883** |                   **NA** |               **NA** |   **16888 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **1000**     |     **37.392 μs** |      **9.595 μs** |     **0.5260 μs** |      **-** |                   **NA** |               **NA** |     **144 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **10000**           | **1**      | **?**           | **?**        |  **3,139.735 μs** |  **2,030.233 μs** |   **111.2839 μs** | **3.9063** |                   **NA** |               **NA** |  **131472 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **1**           | **?**        | **33,188.907 μs** | **17,346.298 μs** |   **950.8092 μs** |      **-** |                    **-** |                **-** |  **132936 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **4**           | **?**        | **51,180.256 μs** | **91,972.679 μs** | **5,041.3329 μs** |      **-** |               **3.0000** |         **313.5000** |  **133570 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **8**           | **?**        | **63,654.790 μs** | **33,159.134 μs** | **1,817.5640 μs** |      **-** |               **7.0000** |         **188.6250** |  **134520 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **10000**           | **10**     | **?**           | **?**        | **30,851.100 μs** | **25,667.994 μs** | **1,406.9494 μs** |      **-** |                   **NA** |               **NA** |  **131472 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **10000**           | **?**      | **?**           | **?**        | **12,021.919 μs** |  **3,950.022 μs** |   **216.5141 μs** |      **-** |                   **NA** |               **NA** |  **262840 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **10000**    |    **352.136 μs** |    **530.260 μs** |    **29.0654 μs** |      **-** |                   **NA** |               **NA** |     **144 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **100000**   |  **4,347.562 μs** |  **5,884.192 μs** |   **322.5324 μs** |      **-** |                   **NA** |               **NA** |     **144 B** |
