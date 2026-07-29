[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $StrykerCliPath,

    [ValidateRange(1, 256)]
    [int] $Concurrency = 1,

    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-StrykerFixture {
    param(
        [Parameter(Mandatory)]
        [string] $CliPath,

        [Parameter(Mandatory)]
        [string] $ProjectDirectory,

        [Parameter(Mandatory)]
        [string] $OutputDirectory,

        [Parameter(Mandatory)]
        [int] $RunnerConcurrency
    )

    $arguments = @(
        $CliPath,
        '--config-file', 'stryker-config.json',
        '--configuration', 'Release',
        '--concurrency', [string]$RunnerConcurrency,
        '--output', $OutputDirectory,
        '--skip-version-check',
        '--verbosity', 'warning'
    )
    Write-Host "[$ProjectDirectory] dotnet $([string]::Join(' ', $arguments))"
    Push-Location $ProjectDirectory

    try {
        & dotnet @arguments

        if ($LASTEXITCODE -ne 0) {
            throw "Stryker exited with code $LASTEXITCODE for '$ProjectDirectory'."
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-NoReporterSidecars {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $AllowedPaths,

        [Parameter(Mandatory)]
        [psobject] $ReporterSidecars
    )

    $pathComparer = if ([OperatingSystem]::IsWindows()) {
        [StringComparer]::OrdinalIgnoreCase
    }
    else {
        [StringComparer]::Ordinal
    }
    $allowed = [System.Collections.Generic.HashSet[string]]::new($pathComparer)

    foreach ($path in $AllowedPaths) {
        $allowed.Add([System.IO.Path]::GetFullPath($path)) | Out-Null
    }

    $htmlExtensions = @($ReporterSidecars.htmlExtensions | ForEach-Object { ([string]$_).ToLowerInvariant() })

    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
        $extension = $file.Extension.ToLowerInvariant()
        $isHtml = $htmlExtensions -contains $extension
        $isReporterJson = $extension -eq '.json' -and $file.BaseName -match ([string]$ReporterSidecars.jsonNamePattern)

        if (($isHtml -or $isReporterJson) -and -not $allowed.Contains($file.FullName)) {
            throw "Unexpected reporter sidecar '$($file.FullName)'."
        }
    }
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or newer is required.'
}

$fixtureRoot = (Resolve-Path $PSScriptRoot).Path
$resolvedCliPath = [System.IO.Path]::GetFullPath($StrykerCliPath)

if (-not (Test-Path -LiteralPath $resolvedCliPath -PathType Leaf)) {
    throw "Stryker CLI was not found at '$resolvedCliPath'."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $resolvedOutputRoot = Join-Path ([System.IO.Path]::GetTempPath()) "stryker-saturation-proof-$([Guid]::NewGuid().ToString('N'))"
}
else {
    $resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
}

if (Test-Path -LiteralPath $resolvedOutputRoot) {
    throw "Output root '$resolvedOutputRoot' already exists. Choose a new path to avoid overwriting evidence."
}

New-Item -ItemType Directory -Path $resolvedOutputRoot | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $fixtureRoot 'fixture-manifest.json') -Raw | ConvertFrom-Json
$controlledProperties = @($manifest.controlledEnvironment.PSObject.Properties)
$previousEnvironment = @{}

try {
    foreach ($property in $controlledProperties) {
        $previousEnvironment[$property.Name] = [Environment]::GetEnvironmentVariable($property.Name, 'Process')
        [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
    }

    Write-Host 'Running fixture preflight and framework smoke validation.'
    & (Join-Path $fixtureRoot 'validate.ps1')

    $xunitOutput = Join-Path $resolvedOutputRoot 'xunit'
    $tunitOutput = Join-Path $resolvedOutputRoot 'tunit'
    Invoke-StrykerFixture -CliPath $resolvedCliPath -ProjectDirectory (Join-Path $fixtureRoot 'Tests.XUnit') -OutputDirectory $xunitOutput -RunnerConcurrency $Concurrency
    Invoke-StrykerFixture -CliPath $resolvedCliPath -ProjectDirectory (Join-Path $fixtureRoot 'Tests.TUnit') -OutputDirectory $tunitOutput -RunnerConcurrency $Concurrency

    $xunitReport = Join-Path $xunitOutput 'reports/mutation-report.json'
    $tunitReport = Join-Path $tunitOutput 'reports/mutation-report.json'

    foreach ($report in @($xunitReport, $tunitReport)) {
        if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
            throw "Expected Stryker report '$report' was not produced."
        }
    }

    Assert-NoReporterSidecars -Root $resolvedOutputRoot -AllowedPaths @($xunitReport, $tunitReport) -ReporterSidecars $manifest.reporterSidecars
    Assert-NoReporterSidecars -Root $fixtureRoot -AllowedPaths @() -ReporterSidecars $manifest.reporterSidecars
    Write-Host "Mutation reports retained at '$resolvedOutputRoot'."
    & (Join-Path $fixtureRoot 'validate.ps1') -XUnitStrykerReportPath $xunitReport -TUnitStrykerReportPath $tunitReport
}
finally {
    foreach ($property in $controlledProperties) {
        [Environment]::SetEnvironmentVariable($property.Name, $previousEnvironment[$property.Name], 'Process')
    }
}
