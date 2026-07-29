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
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **100**             | **1**      | **?**           | **?**        |      **1.220 μs** |   **0.0097 μs** |   **0.0081 μs** |  **0.0744** |                   **NA** |               **NA** |    **1256 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **100**             | **10**     | **?**           | **?**        |     **10.852 μs** |   **0.2166 μs** |   **0.2128 μs** |  **0.0610** |                   **NA** |               **NA** |    **1256 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **100**             | **?**      | **?**           | **?**        |      **2.741 μs** |   **0.0262 μs** |   **0.0219 μs** |  **0.1411** |                   **NA** |               **NA** |    **2408 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **1000**            | **1**      | **?**           | **?**        |     **26.202 μs** |   **0.2349 μs** |   **0.1962 μs** |  **0.4883** |                   **NA** |               **NA** |    **8496 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **1**           | **?**        |    **263.339 μs** |   **2.1080 μs** |   **1.8687 μs** |  **0.4883** |                    **-** |                **-** |    **9960 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **4**           | **?**        |    **468.414 μs** |   **9.0605 μs** |  **13.5613 μs** |  **0.4883** |               **3.0000** |           **5.1978** |   **10577 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **1000**            | **?**      | **8**           | **?**        |    **613.104 μs** |  **10.5841 μs** |   **9.9004 μs** |       **-** |               **7.0000** |          **10.4922** |   **11486 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **1000**            | **10**     | **?**           | **?**        |    **253.645 μs** |   **3.8879 μs** |   **3.4465 μs** |  **0.4883** |                   **NA** |               **NA** |    **8496 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **1000**            | **?**      | **?**           | **?**        |     **75.920 μs** |   **1.4797 μs** |   **2.0743 μs** |  **0.9766** |                   **NA** |               **NA** |   **16888 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **1000**     |      **9.254 μs** |   **0.1365 μs** |   **0.1210 μs** |       **-** |                   **NA** |               **NA** |     **144 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **10000**           | **1**      | **?**           | **?**        |  **1,835.317 μs** |  **19.9783 μs** |  **16.6828 μs** |  **7.8125** |                   **NA** |               **NA** |  **131472 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **1**           | **?**        | **18,734.405 μs** | **295.1053 μs** | **276.0417 μs** |       **-** |                    **-** |                **-** |  **132936 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **4**           | **?**        | **31,069.826 μs** | **514.8865 μs** | **528.7505 μs** |       **-** |               **3.0000** |         **303.9688** |  **133572 B** |
| **RegisterCoverageConcurrentBenchmarks**      | **CaptureCoverageConcurrently**     | **10000**           | **?**      | **8**           | **?**        | **31,939.486 μs** | **528.9942 μs** | **566.0179 μs** |       **-** |               **7.0000** |         **276.9286** |  **134468 B** |
| **RegisterCoverageSequentialBenchmarks**      | **CaptureCoverageGeneration**       | **10000**           | **10**     | **?**           | **?**        | **18,813.435 μs** | **356.2827 μs** | **410.2956 μs** |       **-** |                   **NA** |               **NA** |  **131472 B** |
| **RegisterCoverageStaticPromotionBenchmarks** | **CaptureNormalThenStaticCoverage** | **10000**           | **?**      | **?**           | **?**        |  **5,675.248 μs** |  **93.7719 μs** |  **87.7143 μs** | **15.6250** |                   **NA** |               **NA** |  **262840 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **10000**    |     **92.356 μs** |   **1.2960 μs** |   **1.0823 μs** |       **-** |                   **NA** |               **NA** |     **144 B** |
| **RegisterCoverageDuplicateBenchmarks**       | **CaptureRepeatedMutant**           | **?**               | **?**      | **?**           | **100000**   |    **890.778 μs** |   **9.1172 μs** |   **8.5282 μs** |       **-** |                   **NA** |               **NA** |     **144 B** |
