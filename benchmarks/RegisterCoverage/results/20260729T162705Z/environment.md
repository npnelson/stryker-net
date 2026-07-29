## RegisterCoverage benchmark environment

- UTC run: `20260729T162705Z`
- Harness: `07750cf1219e601b07ed87da74e1b5768684fb39`
- Baseline: `7287834d132a30ee5f6dba1d01d0d9853a0467f9`
- Candidate: `5dbc28697896f98f2d0fff0abd9f28d6f2e15e42`
- Baseline worktree: `/home/npnelson/src/stryker-register-baseline`
- Candidate worktree: `/home/npnelson/src/stryker-register-generation-dictionary`
- BenchmarkDotNet arguments:

```text
--filter \*RegisterCoverage\* --cli /usr/bin/dotnet
```

```text
.NET SDK:
 Version:           10.0.110
 Commit:            f7d90799ce
 Workload version:  10.0.100-manifests.1641d827
 MSBuild version:   18.0.11+f7d90799c

Runtime Environment:
 OS Name:     ubuntu
 OS Version:  24.04
 OS Platform: Linux
 RID:         ubuntu.24.04-x64
 Base Path:   /usr/lib/dotnet/sdk/10.0.110/

.NET workloads installed:
There are no installed workloads to display.
Configured to use workload sets when installing new manifests.
No workload sets are installed. Run "dotnet workload restore" to install a workload set.

Host:
  Version:      10.0.10
  Architecture: x64
  Commit:       f7d90799ce

.NET SDKs installed:
  10.0.110 [/usr/lib/dotnet/sdk]

.NET runtimes installed:
  Microsoft.AspNetCore.App 10.0.10 [/usr/lib/dotnet/shared/Microsoft.AspNetCore.App]
  Microsoft.NETCore.App 10.0.10 [/usr/lib/dotnet/shared/Microsoft.NETCore.App]

Other architectures found:
  None

Environment variables:
  DOTNET_BUNDLE_EXTRACT_BASE_DIR           [/home/npnelson/.cache/dotnet_bundle_extract]

global.json file:
  Not found

Learn more:
  https://aka.ms/dotnet/info

Download .NET:
  https://aka.ms/dotnet/download
```
