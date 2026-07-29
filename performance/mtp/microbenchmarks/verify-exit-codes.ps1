param(
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$assemblyName = "Stryker.Performance.Mtp.Microbenchmarks.dll"
$assemblyPath = Join-Path $PSScriptRoot "bin/$Configuration/net10.0/$assemblyName"

if (-not (Test-Path -LiteralPath $assemblyPath))
{
  throw "Build the benchmark project before running exit-code verification. Missing '$assemblyPath'."
}

$cases = @(
  [pscustomobject]@{
    Name = "workload validation"
    ExpectedExitCode = 0
    ExpectedOutput = "contract-sha256="
    Arguments = @("--validate-workloads")
  }
  [pscustomobject]@{
    Name = "benchmark list"
    ExpectedExitCode = 0
    ExpectedOutput = "HarnessVerificationBenchmark.ReadProfileCount"
    Arguments = @("--list", "flat")
  }
  [pscustomobject]@{
    Name = "invalid BenchmarkDotNet option"
    ExpectedExitCode = 1
    ExpectedOutput = "not-a-real-benchmark-option"
    Arguments = @("--not-a-real-benchmark-option")
  }
  [pscustomobject]@{
    Name = "harness verification"
    ExpectedExitCode = 0
    ExpectedOutput = "Job=harness-verification"
    Arguments = @("--verify-harness")
  }
)

Push-Location $PSScriptRoot

try
{
  foreach ($case in $cases)
  {
    Write-Host "Verifying $($case.Name)..."
    $commandOutput = & dotnet $assemblyPath @($case.Arguments) 2>&1
    $actualExitCode = $LASTEXITCODE
    $outputText = $commandOutput -join [Environment]::NewLine

    if ($actualExitCode -ne $case.ExpectedExitCode)
    {
      $commandOutput | Write-Host
      throw "Case '$($case.Name)' expected exit $($case.ExpectedExitCode), actual $actualExitCode."
    }

    if (-not $outputText.Contains($case.ExpectedOutput, [StringComparison]::Ordinal))
    {
      $commandOutput | Write-Host
      throw "Case '$($case.Name)' did not contain expected output '$($case.ExpectedOutput)'."
    }

    Write-Host "PASS: $($case.Name) exited $actualExitCode."
  }
}
finally
{
  Pop-Location
}

Write-Host "Verified $($cases.Count) subprocess exit-code scenarios."
