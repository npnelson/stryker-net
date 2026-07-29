[CmdletBinding()]
param(
    [string] $XUnitStrykerReportPath,
    [string] $TUnitStrykerReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [object] $Actual,

        [Parameter(Mandatory)]
        [AllowNull()]
        [object] $Expected,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ($Actual -ne $Expected) {
        throw "$Description expected '$Expected', but found '$Actual'."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not $Condition) {
        throw $Description
    }
}

function Assert-SequenceEqual {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Actual,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Expected,

        [Parameter(Mandatory)]
        [string] $Description
    )

    Assert-Equal -Actual $Actual.Count -Expected $Expected.Count -Description "$Description count"

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-Equal -Actual ([string]$Actual[$index]) -Expected ([string]$Expected[$index]) -Description "$Description item $index"
    }
}

function Get-NormalizedText {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $crlf = [string][char]13 + [char]10
    $lf = [string][char]10
    return [System.IO.File]::ReadAllText($Path).Replace($crlf, $lf).Replace([string][char]13, $lf)
}

function Get-Sha256Text {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value
    )

    $hasher = [System.Security.Cryptography.SHA256]::Create()

    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return [Convert]::ToHexString($hasher.ComputeHash($bytes)).ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Assert-TextEqual {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Actual,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ($Actual -eq $Expected) {
        return
    }

    $firstDifference = [Math]::Min($Actual.Length, $Expected.Length)

    for ($index = 0; $index -lt $firstDifference; $index++) {
        if ($Actual[$index] -ne $Expected[$index]) {
            $firstDifference = $index
            break
        }
    }

    $actualHash = Get-Sha256Text -Value $Actual
    $expectedHash = Get-Sha256Text -Value $Expected
    throw "$Description differs at offset $firstDifference (expected length $($Expected.Length), SHA-256 $expectedHash; actual length $($Actual.Length), SHA-256 $actualHash)."
}

function Get-SafeFixturePath {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $platformRelativePath = $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $platformRelativePath))
    $rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else {
        [StringComparison]::Ordinal
    }

    if (-not $resolvedPath.StartsWith($rootPrefix, $comparison)) {
        throw "Fixture input '$RelativePath' escapes '$resolvedRoot'."
    }

    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Fixture input '$RelativePath' does not exist."
    }

    return $resolvedPath
}

function Get-AggregateTextHash {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string[]] $RelativePaths,

        [Parameter(Mandatory)]
        [psobject] $ControlledEnvironment
    )

    $hashLines = @(
        foreach ($relativePath in @($RelativePaths | Sort-Object)) {
            $fullPath = Get-SafeFixturePath -Root $Root -RelativePath $relativePath
            $normalizedPath = $relativePath.Replace('\', '/')
            $contentHash = Get-Sha256Text -Value (Get-NormalizedText -Path $fullPath)
            "file:$normalizedPath=$contentHash"
        }
    )

    foreach ($property in @($ControlledEnvironment.PSObject.Properties | Sort-Object Name)) {
        $hashLines += "env:$($property.Name)=$([string]$property.Value)"
    }

    return Get-Sha256Text -Value ([string]::Join([char]10, $hashLines))
}

function Assert-InputInventory {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string[]] $ExpectedRelativePaths
    )

    $actualRelativePaths = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            ForEach-Object {
                [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
            } |
            Where-Object {
                $_ -notmatch '(^|/)(bin|obj|StrykerOutput)(/|$)'
            } |
            Sort-Object
    )
    $expected = @($ExpectedRelativePaths | Sort-Object)
    Assert-SequenceEqual -Actual $actualRelativePaths -Expected $expected -Description 'Fixture input inventory'
}

function Assert-LockVersion {
    param(
        [Parameter(Mandatory)]
        [string] $LockPath,

        [Parameter(Mandatory)]
        [string] $TargetFramework,

        [Parameter(Mandatory)]
        [string] $DependencyName,

        [Parameter(Mandatory)]
        [string] $ExpectedVersion
    )

    $lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
    $framework = $lock.dependencies.PSObject.Properties[$TargetFramework]

    if ($null -eq $framework) {
        throw "Lock file '$LockPath' does not contain '$TargetFramework'."
    }

    $dependency = $framework.Value.PSObject.Properties[$DependencyName]

    if ($null -eq $dependency) {
        throw "Lock file '$LockPath' does not contain '$DependencyName'."
    }

    Assert-Equal -Actual ([string]$dependency.Value.resolved) -Expected $ExpectedVersion -Description "$DependencyName resolved version"
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $WorkingDirectory
    )

    Write-Host "[$WorkingDirectory] dotnet $([string]::Join(' ', $ArgumentList))"
    Push-Location $WorkingDirectory

    try {
        & dotnet @ArgumentList

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet exited with code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-DotNetCapture {
    param(
        [Parameter(Mandatory)]
        [string[]] $ArgumentList,

        [Parameter(Mandatory)]
        [string] $WorkingDirectory
    )

    Push-Location $WorkingDirectory

    try {
        $output = @(& dotnet @ArgumentList 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        $details = $output | Out-String
        throw "dotnet $([string]::Join(' ', $ArgumentList)) exited with code $exitCode.$([Environment]::NewLine)$details"
    }

    return ($output | Out-String).Trim()
}

function Assert-PathSuffix {
    param(
        [Parameter(Mandatory)]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $ExpectedRelativePath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $normalizedActual = $Actual.Replace('\', '/').TrimEnd('/')
    $normalizedExpected = $ExpectedRelativePath.Replace('\', '/').TrimStart('/')

    if (
        -not $normalizedActual.Equals($normalizedExpected, [StringComparison]::Ordinal) -and
        -not $normalizedActual.EndsWith("/$normalizedExpected", [StringComparison]::Ordinal)
    ) {
        throw "$Description expected path suffix '$normalizedExpected', but found '$Actual'."
    }
}

function Assert-Location {
    param(
        [Parameter(Mandatory)]
        [psobject] $Location,

        [Parameter(Mandatory)]
        [int] $StartLine,

        [Parameter(Mandatory)]
        [int] $StartColumn,

        [Parameter(Mandatory)]
        [int] $EndLine,

        [Parameter(Mandatory)]
        [int] $EndColumn,

        [Parameter(Mandatory)]
        [string] $Description
    )

    Assert-Equal -Actual ([int]$Location.start.line) -Expected $StartLine -Description "$Description start line"
    Assert-Equal -Actual ([int]$Location.start.column) -Expected $StartColumn -Description "$Description start column"
    Assert-Equal -Actual ([int]$Location.end.line) -Expected $EndLine -Description "$Description end line"
    Assert-Equal -Actual ([int]$Location.end.column) -Expected $EndColumn -Description "$Description end column"
}

function Read-JsonDocument {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)

    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Description was not found at '$resolvedPath'."
    }

    if ((Get-Item -LiteralPath $resolvedPath).Length -gt 16MB) {
        throw "$Description exceeds the 16 MiB validation limit."
    }

    try {
        return Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "$Description is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-SingleEntryBySuffix {
    param(
        [Parameter(Mandatory)]
        [object[]] $Entries,

        [Parameter(Mandatory)]
        [string] $ExpectedRelativePath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $matches = @(
        $Entries | Where-Object {
            $normalized = $_.Name.Replace('\', '/')
            $expected = $ExpectedRelativePath.Replace('\', '/')
            $normalized.Equals($expected, [StringComparison]::Ordinal) -or
                $normalized.EndsWith("/$expected", [StringComparison]::Ordinal)
        }
    )
    Assert-Equal -Actual $matches.Count -Expected 1 -Description "$Description matching entry count"

    return $matches[0]
}

function Assert-NoReporterSidecars {
    param(
        [Parameter(Mandatory)]
        [string[]] $Roots,

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
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $allowed.Add([System.IO.Path]::GetFullPath($path)) | Out-Null
        }
    }

    $htmlExtensions = @($ReporterSidecars.htmlExtensions | ForEach-Object { ([string]$_).ToLowerInvariant() })

    foreach ($root in @($Roots | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
            $extension = $file.Extension.ToLowerInvariant()
            $isHtml = $htmlExtensions -contains $extension
            $isReporterJson = $extension -eq '.json' -and $file.BaseName -match ([string]$ReporterSidecars.jsonNamePattern)

            if (($isHtml -or $isReporterJson) -and -not $allowed.Contains($file.FullName)) {
                throw "Unexpected reporter sidecar '$($file.FullName)'."
            }
        }
    }
}

function Assert-StrykerReport {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [ValidateSet('xunit', 'tunit')]
        [string] $FrameworkKey,

        [Parameter(Mandatory)]
        [string] $FixtureRoot,

        [Parameter(Mandatory)]
        [psobject] $Manifest,

        [Parameter(Mandatory)]
        [string] $TargetSource
    )

    $framework = $Manifest.frameworks.PSObject.Properties[$FrameworkKey].Value
    $description = "$FrameworkKey Stryker report"
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $report = Read-JsonDocument -Path $resolvedPath -Description $description

    if ($null -eq $report.PSObject.Properties['files'] -or $null -eq $report.PSObject.Properties['testFiles']) {
        throw "$description does not contain files and testFiles."
    }

    Assert-Equal -Actual ([string]$report.schemaVersion) -Expected ([string]$Manifest.expectedReport.schemaVersion) -Description "$description schema version"
    Assert-PathSuffix -Actual ([string]$report.projectRoot) -ExpectedRelativePath 'TargetProject' -Description "$description project root"

    $sourceEntries = @($report.files.PSObject.Properties)
    Assert-Equal -Actual $sourceEntries.Count -Expected 1 -Description "$description mutated source-file count"
    $sourceEntry = $sourceEntries[0]
    Assert-PathSuffix -Actual $sourceEntry.Name -ExpectedRelativePath ([string]$Manifest.expectedReport.targetSourcePath) -Description "$description mutated source"
    Assert-Equal -Actual ([string]$sourceEntry.Value.language) -Expected 'cs' -Description "$description target language"
    $embeddedTargetSource = ([string]$sourceEntry.Value.source).Replace(([string][char]13 + [char]10), [string][char]10).Replace([string][char]13, [string][char]10)
    Assert-TextEqual -Actual $embeddedTargetSource -Expected $TargetSource -Description "$description embedded target source"

    $testEntries = @($report.testFiles.PSObject.Properties)
    Assert-Equal -Actual $testEntries.Count -Expected 2 -Description "$description test-file count"
    $projectDirectory = [string]$framework.projectDirectory
    $assemblyRelativePath = "$projectDirectory/AssemblyInfo.cs"
    $testRelativePath = "$projectDirectory/SaturationTests.cs"
    $assemblyEntry = Get-SingleEntryBySuffix -Entries $testEntries -ExpectedRelativePath $assemblyRelativePath -Description "$description assembly source"
    $testEntry = Get-SingleEntryBySuffix -Entries $testEntries -ExpectedRelativePath $testRelativePath -Description "$description test source"

    Assert-Equal -Actual ([string]$assemblyEntry.Value.language) -Expected 'cs' -Description "$description assembly language"
    Assert-Equal -Actual ([string]$testEntry.Value.language) -Expected 'cs' -Description "$description test language"
    $embeddedAssemblySource = ([string]$assemblyEntry.Value.source).Replace(([string][char]13 + [char]10), [string][char]10).Replace([string][char]13, [string][char]10)
    $embeddedTestSource = ([string]$testEntry.Value.source).Replace(([string][char]13 + [char]10), [string][char]10).Replace([string][char]13, [string][char]10)
    Assert-TextEqual -Actual $embeddedAssemblySource -Expected (Get-NormalizedText -Path (Get-SafeFixturePath -Root $FixtureRoot -RelativePath $assemblyRelativePath)) -Description "$description embedded assembly source"
    Assert-TextEqual -Actual $embeddedTestSource -Expected (Get-NormalizedText -Path (Get-SafeFixturePath -Root $FixtureRoot -RelativePath $testRelativePath)) -Description "$description embedded test source"
    Assert-Equal -Actual (@($assemblyEntry.Value.tests).Count) -Expected 0 -Description "$description assembly test count"
    Assert-Equal -Actual (@($testEntry.Value.tests).Count) -Expected 1 -Description "$description logical test count"

    $test = @($testEntry.Value.tests)[0]
    Assert-Equal -Actual ([string]$test.name) -Expected ([string]$framework.testName) -Description "$description test name"
    $testId = [string]$test.id

    if ($null -ne $framework.PSObject.Properties['testId']) {
        Assert-Equal -Actual $testId -Expected ([string]$framework.testId) -Description "$description test id"
    }
    elseif ($null -ne $framework.PSObject.Properties['testIdPattern']) {
        Assert-True -Condition ($testId -cmatch ([string]$framework.testIdPattern)) -Description "$description test id '$testId' does not match the expected framework pattern."
    }
    else {
        throw "$description framework contract does not define testId or testIdPattern."
    }
    Assert-Location -Location $test.location -StartLine ([int]$Manifest.expectedReport.testLocation.startLine) -StartColumn ([int]$Manifest.expectedReport.testLocation.startColumn) -EndLine ([int]$Manifest.expectedReport.testLocation.endLine) -EndColumn ([int]$Manifest.expectedReport.testLocation.endColumn) -Description "$description test"

    $mutants = @($sourceEntry.Value.mutants)
    Assert-Equal -Actual $mutants.Count -Expected ([int]$Manifest.expectedReport.totalMutants) -Description "$description mutant count"
    $mutantsById = @{}

    foreach ($mutant in $mutants) {
        $id = [string]$mutant.id

        if ($mutantsById.ContainsKey($id)) {
            throw "$description contains duplicate mutant id '$id'."
        }

        $mutantsById[$id] = $mutant
    }

    $blockExpectation = $Manifest.expectedReport.blockRemoval
    $blockId = [string]$blockExpectation.id

    if (-not $mutantsById.ContainsKey($blockId)) {
        throw "$description does not contain block-removal mutant '$blockId'."
    }

    $block = $mutantsById[$blockId]
    $blockDescription = if ($null -eq $block.PSObject.Properties['description']) { '' } else { [string]$block.description }
    Assert-Equal -Actual ([string]$block.mutatorName) -Expected 'Block removal mutation' -Description "$description block mutator"
    Assert-Equal -Actual $blockDescription -Expected ([string]$blockExpectation.description) -Description "$description block description"
    Assert-Equal -Actual ([string]$block.replacement) -Expected ([string]$blockExpectation.replacement) -Description "$description block replacement"
    Assert-Equal -Actual ([string]$block.status) -Expected ([string]$blockExpectation.status) -Description "$description block status"
    Assert-Equal -Actual ([string]$block.statusReason) -Expected ([string]$blockExpectation.statusReason) -Description "$description block status reason"
    Assert-Equal -Actual ([bool]$block.static) -Expected $false -Description "$description block static flag"
    Assert-Equal -Actual (@($block.coveredBy).Count) -Expected 0 -Description "$description block coveredBy count"
    Assert-Equal -Actual (@($block.killedBy).Count) -Expected 0 -Description "$description block killedBy count"
    Assert-Location -Location $block.location -StartLine ([int]$blockExpectation.startLine) -StartColumn ([int]$blockExpectation.startColumn) -EndLine ([int]$blockExpectation.endLine) -EndColumn ([int]$blockExpectation.endColumn) -Description "$description block mutant"

    $assignmentExpectation = $Manifest.expectedReport.assignmentMutations
    $assignmentMutator = 'AddAssignmentExpression to SubtractAssignmentExpression mutation'

    for ($operand = [int]$assignmentExpectation.firstId; $operand -le [int]$assignmentExpectation.lastId; $operand++) {
        $id = [string]$operand

        if (-not $mutantsById.ContainsKey($id)) {
            throw "$description does not contain assignment mutant '$id'."
        }

        $mutant = $mutantsById[$id]
        $expectedLine = [int]$assignmentExpectation.firstLine + $operand - 1
        $expectedOriginal = "checksum += $operand"
        $mutantDescription = if ($null -eq $mutant.PSObject.Properties['description']) { '' } else { [string]$mutant.description }
        Assert-Equal -Actual ([string]$mutant.mutatorName) -Expected $assignmentMutator -Description "$description mutant $id mutator"
        Assert-Equal -Actual $mutantDescription -Expected ([string]$assignmentExpectation.description) -Description "$description mutant $id description"
        Assert-Equal -Actual ([string]$mutant.replacement) -Expected "checksum -= $operand" -Description "$description mutant $id replacement"
        Assert-Equal -Actual ([string]$mutant.status) -Expected 'Killed' -Description "$description mutant $id status"
        Assert-Equal -Actual ([bool]$mutant.static) -Expected $false -Description "$description mutant $id static flag"
        Assert-Equal -Actual (@($mutant.coveredBy).Count) -Expected 0 -Description "$description mutant $id coveredBy count"
        $killedBy = @($mutant.killedBy)
        Assert-Equal -Actual $killedBy.Count -Expected 1 -Description "$description mutant $id killedBy count"
        Assert-Equal -Actual ([string]$killedBy[0]) -Expected $testId -Description "$description mutant $id killer"
        Assert-Location -Location $mutant.location -StartLine $expectedLine -StartColumn ([int]$assignmentExpectation.startColumn) -EndLine $expectedLine -EndColumn ([int]$assignmentExpectation.startColumn + $expectedOriginal.Length) -Description "$description mutant $id"
    }

    $statusCounts = @{}

    foreach ($group in @($mutants | Group-Object status)) {
        $statusCounts[$group.Name] = $group.Count
    }

    Assert-Equal -Actual $statusCounts.Count -Expected 2 -Description "$description distinct status count"
    Assert-Equal -Actual ([int]$statusCounts['Killed']) -Expected ([int]$Manifest.expectedReport.statuses.Killed) -Description "$description killed count"
    Assert-Equal -Actual ([int]$statusCounts['Ignored']) -Expected ([int]$Manifest.expectedReport.statuses.Ignored) -Description "$description ignored count"

    $mutatorCounts = @{}

    foreach ($group in @($mutants | Group-Object mutatorName)) {
        $mutatorCounts[$group.Name] = $group.Count
    }

    Assert-Equal -Actual $mutatorCounts.Count -Expected 2 -Description "$description distinct mutator count"
    Assert-Equal -Actual ([int]$mutatorCounts[$assignmentMutator]) -Expected ([int]$Manifest.expectedReport.mutators.$assignmentMutator) -Description "$description assignment mutator count"
    Assert-Equal -Actual ([int]$mutatorCounts['Block removal mutation']) -Expected ([int]$Manifest.expectedReport.mutators.'Block removal mutation') -Description "$description block mutator count"

    $canonicalMutants = for ($id = 0; $id -le [int]$assignmentExpectation.lastId; $id++) {
        $mutant = $mutantsById[[string]$id]
        [ordered]@{
            id = [string]$mutant.id
            mutatorName = [string]$mutant.mutatorName
            description = if ($null -eq $mutant.PSObject.Properties['description']) { '' } else { [string]$mutant.description }
            replacement = [string]$mutant.replacement
            location = [ordered]@{
                start = [ordered]@{
                    line = [int]$mutant.location.start.line
                    column = [int]$mutant.location.start.column
                }
                end = [ordered]@{
                    line = [int]$mutant.location.end.line
                    column = [int]$mutant.location.end.column
                }
            }
            status = [string]$mutant.status
            statusReason = if ($null -eq $mutant.PSObject.Properties['statusReason']) { '' } else { [string]$mutant.statusReason }
            static = [bool]$mutant.static
            coveredBy = @($mutant.coveredBy)
            killedBy = @($mutant.killedBy | ForEach-Object { 'fixture-test' })
        }
    }
    $canonicalDocument = [ordered]@{
        schemaVersion = [string]$report.schemaVersion
        targetSourcePath = [string]$Manifest.expectedReport.targetSourcePath
        targetSourceSha256 = Get-Sha256Text -Value $TargetSource
        mutants = $canonicalMutants
    }
    $canonicalJson = $canonicalDocument | ConvertTo-Json -Depth 12 -Compress

    return [pscustomobject]@{
        framework = $FrameworkKey
        path = $resolvedPath
        testName = [string]$test.name
        testId = [string]$test.id
        totalMutants = $mutants.Count
        killed = [int]$statusCounts['Killed']
        ignored = [int]$statusCounts['Ignored']
        canonicalMutationSha256 = Get-Sha256Text -Value $canonicalJson
    }
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 or newer is required.'
}

$hasXUnitReport = -not [string]::IsNullOrWhiteSpace($XUnitStrykerReportPath)
$hasTUnitReport = -not [string]::IsNullOrWhiteSpace($TUnitStrykerReportPath)

if ($hasXUnitReport -xor $hasTUnitReport) {
    throw 'XUnitStrykerReportPath and TUnitStrykerReportPath must be supplied together.'
}

$fixtureRoot = (Resolve-Path $PSScriptRoot).Path
$manifestPath = Join-Path $fixtureRoot 'fixture-manifest.json'
$manifest = Read-JsonDocument -Path $manifestPath -Description 'Fixture manifest'
Assert-Equal -Actual ([string]$manifest.comparisonScope) -Expected 'whole-stack' -Description 'Comparison scope'
$controlledProperties = @($manifest.controlledEnvironment.PSObject.Properties)
$previousEnvironment = @{}
$resultRoot = $null

try {
    foreach ($property in $controlledProperties) {
        $previousEnvironment[$property.Name] = [Environment]::GetEnvironmentVariable($property.Name, 'Process')
        [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
    }

    Assert-InputInventory -Root $fixtureRoot -ExpectedRelativePaths @($manifest.fingerprints.fixtureInputs)
    $semanticSha256 = Get-AggregateTextHash -Root $fixtureRoot -RelativePaths @($manifest.fingerprints.semanticInputs) -ControlledEnvironment $manifest.controlledEnvironment
    $fixtureInputSha256 = Get-AggregateTextHash -Root $fixtureRoot -RelativePaths @($manifest.fingerprints.fixtureInputs) -ControlledEnvironment $manifest.controlledEnvironment

    $targetSourcePath = Get-SafeFixturePath -Root $fixtureRoot -RelativePath ([string]$manifest.expectedReport.targetSourcePath)
    $targetSource = Get-NormalizedText -Path $targetSourcePath
    $oracle = Get-NormalizedText -Path (Get-SafeFixturePath -Root $fixtureRoot -RelativePath 'CommonTests/SaturationOracle.cs')
    $siteMatches = [regex]::Matches($targetSource, '(?m)^        checksum \+= (?<operand>[1-9][0-9]*);$')
    Assert-Equal -Actual $siteMatches.Count -Expected ([int]$manifest.declaredAssignmentMutationSites) -Description 'Static preflight assignment-site count'
    $operandSum = 0

    for ($index = 0; $index -lt $siteMatches.Count; $index++) {
        $operand = [int]$siteMatches[$index].Groups['operand'].Value
        $expectedOperand = $index + 1
        $line = $targetSource.Substring(0, $siteMatches[$index].Index).Split([char]10).Count
        Assert-Equal -Actual $operand -Expected $expectedOperand -Description "Static preflight operand $expectedOperand"
        Assert-Equal -Actual $line -Expected ([int]$manifest.expectedReport.assignmentMutations.firstLine + $index) -Description "Static preflight line for operand $expectedOperand"
        $operandSum += $operand
    }

    Assert-Equal -Actual ([int]$manifest.seed + $operandSum) -Expected ([int]$manifest.expectedChecksum) -Description 'Static preflight checksum'
    $seedMatch = [regex]::Match($oracle, 'public const int Seed = (?<value>[0-9_]+);')
    $checksumMatch = [regex]::Match($oracle, 'public const int ExpectedChecksum = (?<value>[0-9_]+);')
    Assert-True -Condition ($seedMatch.Success -and $checksumMatch.Success) -Description 'Shared oracle constants could not be read.'
    Assert-Equal -Actual ([int]$seedMatch.Groups['value'].Value.Replace('_', '')) -Expected ([int]$manifest.seed) -Description 'Oracle seed'
    Assert-Equal -Actual ([int]$checksumMatch.Groups['value'].Value.Replace('_', '')) -Expected ([int]$manifest.expectedChecksum) -Description 'Oracle checksum'

    foreach ($frameworkKey in @('xunit', 'tunit')) {
        $framework = $manifest.frameworks.PSObject.Properties[$frameworkKey].Value
        $configPath = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($framework.projectDirectory)/stryker-config.json"
        $config = (Read-JsonDocument -Path $configPath -Description "$frameworkKey Stryker config").'stryker-config'
        Assert-Equal -Actual ([string]$config.'test-runner') -Expected ([string]$manifest.strykerConfiguration.testRunner) -Description "$frameworkKey test runner"
        Assert-Equal -Actual ([string]$config.project) -Expected 'SchedulerSaturation.TargetProject.csproj' -Description "$frameworkKey target project"
        Assert-Equal -Actual ([string]$config.'mutation-level') -Expected ([string]$manifest.strykerConfiguration.mutationLevel) -Description "$frameworkKey mutation level"
        Assert-Equal -Actual ([string]$config.'coverage-analysis') -Expected ([string]$manifest.strykerConfiguration.coverageAnalysis) -Description "$frameworkKey coverage analysis"
        Assert-Equal -Actual ([bool]$config.'disable-bail') -Expected ([bool]$manifest.strykerConfiguration.disableBail) -Description "$frameworkKey disable bail"
        Assert-Equal -Actual ([bool]$config.'disable-mix-mutants') -Expected ([bool]$manifest.strykerConfiguration.disableMixMutants) -Description "$frameworkKey disable mutant mixing"
        Assert-SequenceEqual -Actual @($config.reporters) -Expected @($manifest.strykerConfiguration.reporters) -Description "$frameworkKey reporters"
        Assert-SequenceEqual -Actual @($config.mutate) -Expected @($manifest.strykerConfiguration.mutate) -Description "$frameworkKey mutate patterns"
    }

    $xunit = $manifest.frameworks.xunit
    $tunit = $manifest.frameworks.tunit
    $xunitProject = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($xunit.projectDirectory)/$($xunit.projectFile)"
    $tunitProject = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($tunit.projectDirectory)/$($tunit.projectFile)"
    Invoke-DotNet -ArgumentList @('restore', $xunitProject, '--locked-mode') -WorkingDirectory $fixtureRoot
    Invoke-DotNet -ArgumentList @('restore', $tunitProject, '--locked-mode') -WorkingDirectory $fixtureRoot

    $targetFramework = [string]$manifest.packageStack.targetFramework
    $xunitLock = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($xunit.projectDirectory)/packages.lock.json"
    $tunitLock = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($tunit.projectDirectory)/packages.lock.json"
    Assert-LockVersion $xunitLock $targetFramework 'Microsoft.NET.Test.Sdk' ([string]$manifest.packageStack.microsoftNetTestSdk)
    Assert-LockVersion $xunitLock $targetFramework 'Microsoft.Testing.Platform' ([string]$manifest.packageStack.microsoftTestingPlatform)
    Assert-LockVersion $xunitLock $targetFramework 'Microsoft.Testing.Platform.MSBuild' ([string]$manifest.packageStack.microsoftTestingPlatformMsBuild)
    Assert-LockVersion $xunitLock $targetFramework 'xunit.v3.mtp-v2' ([string]$manifest.packageStack.xunitV3MtpV2)
    Assert-LockVersion $tunitLock $targetFramework 'Microsoft.NET.Test.Sdk' ([string]$manifest.packageStack.microsoftNetTestSdk)
    Assert-LockVersion $tunitLock $targetFramework 'Microsoft.Testing.Platform' ([string]$manifest.packageStack.microsoftTestingPlatform)
    Assert-LockVersion $tunitLock $targetFramework 'Microsoft.Testing.Platform.MSBuild' ([string]$manifest.packageStack.microsoftTestingPlatformMsBuild)
    Assert-LockVersion $tunitLock $targetFramework 'TUnit' ([string]$manifest.packageStack.tunit)

    Invoke-DotNet -ArgumentList @('build', $xunitProject, '--configuration', 'Release', '--no-restore', '--no-incremental') -WorkingDirectory $fixtureRoot
    Invoke-DotNet -ArgumentList @('build', $tunitProject, '--configuration', 'Release', '--no-restore', '--no-incremental') -WorkingDirectory $fixtureRoot

    $outputDirectory = "bin/Release/$targetFramework"
    $xunitDll = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($xunit.projectDirectory)/$outputDirectory/SchedulerSaturation.Tests.XUnit.dll"
    $tunitDll = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($tunit.projectDirectory)/$outputDirectory/SchedulerSaturation.Tests.TUnit.dll"
    $targetDll = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "TargetProject/$outputDirectory/SchedulerSaturation.TargetProject.dll"
    $xunitTargetDll = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($xunit.projectDirectory)/$outputDirectory/SchedulerSaturation.TargetProject.dll"
    $tunitTargetDll = Get-SafeFixturePath -Root $fixtureRoot -RelativePath "$($tunit.projectDirectory)/$outputDirectory/SchedulerSaturation.TargetProject.dll"
    $targetBinarySha256 = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Equal -Actual ((Get-FileHash -LiteralPath $xunitTargetDll -Algorithm SHA256).Hash.ToLowerInvariant()) -Expected $targetBinarySha256 -Description 'xUnit target binary copy'
    Assert-Equal -Actual ((Get-FileHash -LiteralPath $tunitTargetDll -Algorithm SHA256).Hash.ToLowerInvariant()) -Expected $targetBinarySha256 -Description 'TUnit target binary copy'

    $xunitListOutput = Invoke-DotNetCapture -ArgumentList @($xunitDll, '-list', 'tests/json', '-noColor', '-noLogo', '-parallel', 'none', '-maxThreads', '1') -WorkingDirectory $fixtureRoot
    $xunitTests = @($xunitListOutput | ConvertFrom-Json)
    Assert-Equal -Actual $xunitTests.Count -Expected ([int]$manifest.logicalTestCount) -Description 'xUnit smoke discovery count'
    Assert-Equal -Actual ([string]$xunitTests[0]) -Expected ([string]$xunit.testName) -Description 'xUnit smoke test identity'

    $tunitListOutput = Invoke-DotNetCapture -ArgumentList @($tunitDll, '--list-tests', 'json', '--no-ansi', '--progress', 'off', '--disable-logo', '--maximum-parallel-tests', '1') -WorkingDirectory $fixtureRoot
    $tunitList = $tunitListOutput | ConvertFrom-Json
    $tunitTests = @($tunitList.tests)
    Assert-Equal -Actual $tunitTests.Count -Expected ([int]$manifest.logicalTestCount) -Description 'TUnit smoke discovery count'
    $tunitTestName = [string]::Join('.', @($tunitTests[0].type.namespace, $tunitTests[0].type.typeName, $tunitTests[0].type.methodName))
    Assert-Equal -Actual $tunitTestName -Expected ([string]$tunit.testName) -Description 'TUnit smoke test identity'

    $resultRoot = Join-Path ([System.IO.Path]::GetTempPath()) "stryker-saturation-validation-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $resultRoot | Out-Null
    $xunitResultPath = Join-Path $resultRoot 'xunit.xml'
    Invoke-DotNet -ArgumentList @($xunitDll, '-noColor', '-noLogo', '-parallel', 'none', '-maxThreads', '1', '-xml', $xunitResultPath) -WorkingDirectory $fixtureRoot
    [xml]$xunitResult = Get-Content -LiteralPath $xunitResultPath -Raw
    $xunitAssemblies = @($xunitResult.assemblies.assembly)
    Assert-Equal -Actual $xunitAssemblies.Count -Expected 1 -Description 'xUnit smoke result assembly count'
    Assert-Equal -Actual ([int]$xunitAssemblies[0].total) -Expected 1 -Description 'xUnit smoke executed count'
    Assert-Equal -Actual ([int]$xunitAssemblies[0].passed) -Expected 1 -Description 'xUnit smoke passed count'
    Assert-Equal -Actual ([int]$xunitAssemblies[0].failed) -Expected 0 -Description 'xUnit smoke failed count'
    $xunitResults = @($xunitAssemblies[0].collection.test)
    Assert-Equal -Actual $xunitResults.Count -Expected 1 -Description 'xUnit smoke test-result count'
    Assert-Equal -Actual ([string]$xunitResults[0].name) -Expected ([string]$xunit.testName) -Description 'xUnit smoke result identity'

    $tunitResultPath = Join-Path $resultRoot 'tunit.trx'
    Invoke-DotNet -ArgumentList @($tunitDll, '--minimum-expected-tests', '1', '--report-trx', '--report-trx-filename', 'tunit.trx', '--results-directory', $resultRoot, '--no-ansi', '--progress', 'off', '--disable-logo', '--maximum-parallel-tests', '1') -WorkingDirectory $fixtureRoot
    [xml]$tunitResult = Get-Content -LiteralPath $tunitResultPath -Raw
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($tunitResult.NameTable)
    $namespaceManager.AddNamespace('trx', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = $tunitResult.SelectSingleNode('//trx:ResultSummary/trx:Counters', $namespaceManager)
    $tunitTestMethod = $tunitResult.SelectSingleNode('//trx:TestDefinitions/trx:UnitTest/trx:TestMethod', $namespaceManager)
    $tunitUnitTestResult = $tunitResult.SelectSingleNode('//trx:Results/trx:UnitTestResult', $namespaceManager)
    Assert-True -Condition ($null -ne $counters -and $null -ne $tunitTestMethod -and $null -ne $tunitUnitTestResult) -Description 'TUnit TRX lacks required counters or test result.'
    Assert-Equal -Actual ([int]$counters.total) -Expected 1 -Description 'TUnit smoke total count'
    Assert-Equal -Actual ([int]$counters.executed) -Expected 1 -Description 'TUnit smoke executed count'
    Assert-Equal -Actual ([int]$counters.passed) -Expected 1 -Description 'TUnit smoke passed count'
    Assert-Equal -Actual ([int]$counters.failed) -Expected 0 -Description 'TUnit smoke failed count'
    Assert-Equal -Actual ([string]$tunitUnitTestResult.outcome) -Expected 'Passed' -Description 'TUnit smoke outcome'
    Assert-Equal -Actual "$($tunitTestMethod.className).$($tunitTestMethod.name)" -Expected ([string]$tunit.testName) -Description 'TUnit smoke result identity'

    $xunitStrykerResult = $null
    $tunitStrykerResult = $null
    $allowedReporterPaths = @()
    $sidecarRoots = @($fixtureRoot, $resultRoot)

    if ($hasXUnitReport -and $hasTUnitReport) {
        $xunitStrykerResult = Assert-StrykerReport -Path $XUnitStrykerReportPath -FrameworkKey xunit -FixtureRoot $fixtureRoot -Manifest $manifest -TargetSource $targetSource
        $tunitStrykerResult = Assert-StrykerReport -Path $TUnitStrykerReportPath -FrameworkKey tunit -FixtureRoot $fixtureRoot -Manifest $manifest -TargetSource $targetSource
        Assert-Equal -Actual $xunitStrykerResult.canonicalMutationSha256 -Expected $tunitStrykerResult.canonicalMutationSha256 -Description 'Cross-framework canonical mutation fingerprint'
        $allowedReporterPaths = @($xunitStrykerResult.path, $tunitStrykerResult.path)
        $sidecarRoots += @([System.IO.Path]::GetDirectoryName($xunitStrykerResult.path), [System.IO.Path]::GetDirectoryName($tunitStrykerResult.path))
    }

    Assert-NoReporterSidecars -Roots $sidecarRoots -AllowedPaths $allowedReporterPaths -ReporterSidecars $manifest.reporterSidecars

    [ordered]@{
        valid = $true
        profileId = [string]$manifest.profileId
        comparisonScope = [string]$manifest.comparisonScope
        fingerprints = [ordered]@{
            semanticAndEnvironmentSha256 = $semanticSha256
            completeFixtureInputSha256 = $fixtureInputSha256
            rawTargetBinarySha256 = $targetBinarySha256
            rawBinaryIdentityScope = 'Diagnostic for this build only; semantic acceptance uses normalized source, config, lock, and environment inputs.'
        }
        staticPreflight = [ordered]@{
            valid = $true
            declaredAssignmentMutationSites = $siteMatches.Count
            expectedChecksum = [int]$manifest.expectedChecksum
        }
        frameworkSmoke = [ordered]@{
            valid = $true
            proofBoundary = 'Direct discovery and execution smoke only; not evidence of the Stryker MTP --server path.'
            xunit = [ordered]@{ discovered = $xunitTests.Count; passed = [int]$xunitAssemblies[0].passed; test = [string]$xunitTests[0] }
            tunit = [ordered]@{ discovered = $tunitTests.Count; passed = [int]$counters.passed; test = $tunitTestName }
        }
        mutationProof = if ($null -eq $xunitStrykerResult) {
            [ordered]@{ validated = $false; reason = 'Both Stryker reports are required to prove exact mutation equivalence.' }
        }
        else {
            [ordered]@{
                validated = $true
                canonicalMutationSha256 = $xunitStrykerResult.canonicalMutationSha256
                xunit = $xunitStrykerResult
                tunit = $tunitStrykerResult
            }
        }
        timingConclusion = $null
    } | ConvertTo-Json -Depth 12
}
finally {
    if ($null -ne $resultRoot -and (Test-Path -LiteralPath $resultRoot -PathType Container)) {
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        $resolvedResultRoot = [System.IO.Path]::GetFullPath($resultRoot)

        if (-not $resolvedResultRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove validation directory outside '$tempRoot'."
        }

        Remove-Item -LiteralPath $resolvedResultRoot -Recurse -Force
    }

    foreach ($property in $controlledProperties) {
        [Environment]::SetEnvironmentVariable($property.Name, $previousEnvironment[$property.Name], 'Process')
    }
}
