```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD Ryzen 9 7900X 4.69GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.110
  [Host]     : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  Job-JMDAGQ : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Toolchain=.NET 10.0

```
| Type                                      | Method                          | DistinctMutants | Passes | WorkerCount | HitCount | Mean          | Error       | StdDev      | Gen0    | Completed Work Items | Lock Contentions | Allocated |
|------------------------------------------ |-------------------------------- |---------------- |------- |------------ |--------- |--------------:|------------:|------------:|--------:|---------------------:|-----------------:|----------:|
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **100**             | **1**      | **?**           | **?**        |      **1.211 μs** |   **0.0112 μs** |   **0.0105 μs** |  **0.0744** |                   **NA** |               **NA** |    **1256 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **100**             | **10**     | **?**           | **?**        |     **10.583 μs** |   **0.0475 μs** |   **0.0421 μs** |  **0.0610** |                   **NA** |               **NA** |    **1256 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **100**             | **?**      | **?**           | **?**        |      **2.659 μs** |   **0.0115 μs** |   **0.0102 μs** |  **0.1411** |                   **NA** |               **NA** |    **2408 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **1000**            | **1**      | **?**           | **?**        |     **26.093 μs** |   **0.1817 μs** |   **0.1610 μs** |  **0.4883** |                   **NA** |               **NA** |    **8496 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **1**           | **?**        |    **264.222 μs** |   **1.0728 μs** |   **0.9510 μs** |  **0.4883** |                    **-** |                **-** |    **9960 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **4**           | **?**        |    **456.986 μs** |   **8.7097 μs** |   **8.1470 μs** |  **0.4883** |               **3.0000** |           **5.6826** |   **10577 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **8**           | **?**        |    **606.231 μs** |  **11.7822 μs** |  **12.0995 μs** |       **-** |               **7.0000** |          **10.1162** |   **11486 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **1000**            | **10**     | **?**           | **?**        |    **243.994 μs** |   **1.5036 μs** |   **1.3329 μs** |  **0.4883** |                   **NA** |               **NA** |    **8496 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **1000**            | **?**      | **?**           | **?**        |     **69.725 μs** |   **1.2271 μs** |   **1.1479 μs** |  **0.9766** |                   **NA** |               **NA** |   **16888 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **1000**     |      **9.174 μs** |   **0.0944 μs** |   **0.0883 μs** |       **-** |                   **NA** |               **NA** |     **144 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **10000**           | **1**      | **?**           | **?**        |  **1,787.973 μs** |   **6.8297 μs** |   **6.3885 μs** |  **7.8125** |                   **NA** |               **NA** |  **131472 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **1**           | **?**        | **18,807.512 μs** | **204.6859 μs** | **191.4633 μs** |       **-** |                    **-** |                **-** |  **132936 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **4**           | **?**        | **30,096.534 μs** | **219.2270 μs** | **205.0651 μs** |       **-** |               **3.0000** |         **337.6250** |  **133581 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **8**           | **?**        | **30,746.837 μs** | **298.6479 μs** | **264.7436 μs** |       **-** |               **7.0000** |         **324.6875** |  **134468 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **10000**           | **10**     | **?**           | **?**        | **18,303.588 μs** | **129.2244 μs** | **120.8766 μs** |       **-** |                   **NA** |               **NA** |  **131472 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **10000**           | **?**      | **?**           | **?**        |  **5,562.336 μs** |  **39.9271 μs** |  **37.3478 μs** | **15.6250** |                   **NA** |               **NA** |  **262840 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **10000**    |     **91.121 μs** |   **0.5692 μs** |   **0.5324 μs** |       **-** |                   **NA** |               **NA** |     **144 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **100000**   |    **893.200 μs** |   **8.4774 μs** |   **7.9297 μs** |       **-** |                   **NA** |               **NA** |     **144 B** |
