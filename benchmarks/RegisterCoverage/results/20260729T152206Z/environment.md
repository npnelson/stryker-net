## RegisterCoverage benchmark environment

- UTC run: `20260729T152206Z`
- Harness: `b6d3e46835518edc94235ca92d5df8d193a4013c`
- Baseline: `7287834d132a30ee5f6dba1d01d0d9853a0467f9`
- Candidate: `af8634a2ffeb39e2eb027ddaab78de8030a6e894`
- Baseline worktree: `/workspace/scratch/978baef2d26e/stryker-register-benchmark-baseline`
- Candidate worktree: `/workspace/scratch/978baef2d26e/stryker-register-benchmark-candidate`
- BenchmarkDotNet arguments:

```text
--filter \*RegisterCoverage\* --cli /tmp/stryker-dotnet/dotnet --job short
```

```text
.NET SDK:
 Version:           10.0.302
 Commit:            35b593bebf
 Workload version:  10.0.300-manifests.1641d827
 MSBuild version:   18.6.11+35b593beb

Runtime Environment:
 OS Name:     ubuntu
 OS Version:  24.04
 OS Platform: Linux
 RID:         linux-x64
 Base Path:   /tmp/stryker-dotnet/sdk/10.0.302/

.NET workloads installed:
There are no installed workloads to display.
Configured to use workload sets when installing new manifests.
No workload sets are installed. Run "dotnet workload restore" to install a workload set.

Host:
  Version:      10.0.10
  Architecture: x64
  Commit:       f7d90799ce

.NET SDKs installed:
  10.0.302 [/tmp/stryker-dotnet/sdk]

.NET runtimes installed:
  Microsoft.AspNetCore.App 10.0.10 [/tmp/stryker-dotnet/shared/Microsoft.AspNetCore.App]
  Microsoft.NETCore.App 10.0.10 [/tmp/stryker-dotnet/shared/Microsoft.NETCore.App]

Other architectures found:
  None

Environment variables:
  DOTNET_CLI_HOME                          [/tmp/stryker-register-dotnet-home]
  DOTNET_CLI_TELEMETRY_OPTOUT              [1]
  DOTNET_CMD                               [/tmp/stryker-dotnet/dotnet]
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE        [1]

global.json file:
  Not found

Learn more:
  https://aka.ms/dotnet/info

Download .NET:
  https://aka.ms/dotnet/download
```
