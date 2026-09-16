#Requires -Version 5.1
<#
.SYNOPSIS
    Validates the JsonDrift compatibility-rule matrix and canonical contract document.

.DESCRIPTION
    Restores and builds the solution in Release, runs every compatibility-rule check against real
    System.Text.Json behavior, then writes the canonical contract document for the representative root
    contract twice in separate processes and compares the two files byte for byte.

.PARAMETER Configuration
    Build configuration used for the build and for the experiment. Release is the default.

.PARAMETER SkipNuGetAudit
    Skips the NuGet vulnerability audit during restore. Use only when the audit data source is unavailable.

.EXAMPLE
    pwsh scripts/validate-rule-matrix.ps1

.EXAMPLE
    pwsh scripts/validate-rule-matrix.ps1 -Configuration Release -Verbose
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipNuGetAudit
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'KeelMatrix.JsonDrift.sln'
$nugetConfig = Join-Path $repoRoot 'NuGet.config'
$experimentProject = Join-Path $repoRoot 'experiments/KeelMatrix.JsonDrift.RuleMatrix'
$canonicalRoot = Join-Path $repoRoot 'artifacts/canonical'
$canonicalFileName = 'order-envelope.contract.json'
$failures = 0

function Write-Section {
    param([string]$Text)
    Write-Host ''
    Write-Host "== $Text"
}

function Invoke-Step {
    param(
        [string]$Description,
        [string[]]$Command
    )

    Write-Host "dotnet $($Command -join ' ')"
    & dotnet @Command | Write-Host
    $exitCode = $LASTEXITCODE

    Write-Host "exit code: $exitCode"

    if ($exitCode -ne 0) {
        $script:failures++
        Write-Host "FAILED: $Description (exit code $exitCode)"
    }

    return $exitCode
}

function Get-Sha256 {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()

    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

if (-not (Test-Path $solution)) {
    throw "solution not found: $solution"
}

$restoreArguments = @('restore', $solution, '--configfile', $nugetConfig)

if ($SkipNuGetAudit) {
    $restoreArguments += '-p:NuGetAudit=false'
}

Write-Section 'Restore'
Invoke-Step -Description 'restore' -Command $restoreArguments | Out-Null

Write-Section "Build ($Configuration)"
Invoke-Step -Description 'build' -Command @('build', $solution, '-c', $Configuration, '--no-restore') | Out-Null

Write-Section 'Compatibility rule matrix'
$matrixExit = Invoke-Step -Description 'rule matrix' -Command @(
    'run', '--project', $experimentProject, '-c', $Configuration, '--no-build', '--', '--matrix'
)

if ($matrixExit -ne 0) {
    Write-Host 'The rule matrix did not agree with the recorded classifications.'
}

Write-Section 'Canonical contract document determinism'
$runA = Join-Path $canonicalRoot 'run-a'
$runB = Join-Path $canonicalRoot 'run-b'

Remove-Item -LiteralPath $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue

Invoke-Step -Description 'canonical run a' -Command @(
    'run', '--project', $experimentProject, '-c', $Configuration, '--no-build', '--', '--canonical', $runA
) | Out-Null

Invoke-Step -Description 'canonical run b' -Command @(
    'run', '--project', $experimentProject, '-c', $Configuration, '--no-build', '--', '--canonical', $runB
) | Out-Null

$fileA = Join-Path $runA $canonicalFileName
$fileB = Join-Path $runB $canonicalFileName

if (-not (Test-Path $fileA) -or -not (Test-Path $fileB)) {
    $failures++
    Write-Host "FAILED: canonical document was not written to both output directories."
}
else {
    $bytesA = [System.IO.File]::ReadAllBytes($fileA)
    $bytesB = [System.IO.File]::ReadAllBytes($fileB)
    $identical = $bytesA.Length -eq $bytesB.Length

    if ($identical) {
        for ($index = 0; $index -lt $bytesA.Length; $index++) {
            if ($bytesA[$index] -ne $bytesB[$index]) {
                $identical = $false
                break
            }
        }
    }

    Write-Host "file            : $canonicalFileName"
    Write-Host "bytes (run a)   : $($bytesA.Length)"
    Write-Host "bytes (run b)   : $($bytesB.Length)"
    Write-Host "sha256 (run a)  : $(Get-Sha256 -Bytes $bytesA)"
    Write-Host "sha256 (run b)  : $(Get-Sha256 -Bytes $bytesB)"
    Write-Host "identical bytes : $identical"

    if (-not $identical) {
        $failures++
        Write-Host 'FAILED: canonical documents differ between runs.'
    }
}

Remove-Item -LiteralPath $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue

Write-Section 'Result'

if ($failures -gt 0) {
    Write-Host "validation failed: $failures step(s) failed"
    exit 1
}

Write-Host 'validation passed: rule matrix and canonical document determinism are green'
exit 0
