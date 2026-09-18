#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the local CI-equivalent validation gate for the JsonDrift foundation.

.DESCRIPTION
    The required check identifiers are read from validation-manifest.json. Every check records its
    identifier before execution; the final manifest check fails closed when a required identifier was
    omitted from the executed set. This makes deleting a gate invocation in a throwaway copy observable.
#>
[CmdletBinding()]
param(
    [switch]$SkipNuGetAudit
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'KeelMatrix.JsonDrift.sln'
$nugetConfig = Join-Path $repoRoot 'NuGet.config'
$productProject = Join-Path $repoRoot 'src/KeelMatrix.JsonDrift/KeelMatrix.JsonDrift.csproj'
$experimentProject = Join-Path $repoRoot 'experiments/KeelMatrix.JsonDrift.RuleMatrix'
$manifestPath = Join-Path $PSScriptRoot 'validation-manifest.json'
$packageOutput = Join-Path $repoRoot 'artifacts/package'
$canonicalRoot = Join-Path $repoRoot 'artifacts/canonical'
$canonicalFileName = 'order-envelope.contract.json'
$executed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$failures = 0

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Text)
    Write-Host ''
    Write-Host "== $Text"
}

function Invoke-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    if (-not $script:executed.Add($Id)) {
        throw "validation check id was executed more than once: $Id"
    }

    Write-Host "check: $Id"
    try {
        & $Action
        Write-Host "passed: $Description"
    }
    catch {
        $script:failures++
        Write-Host "FAILED: $Description"
        Write-Host $_.Exception.Message
    }
}

function Invoke-Dotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet exited with code $exitCode"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "solution not found: $solution"
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "validation manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$requiredChecks = @($manifest.requiredChecks)
if ($requiredChecks.Count -eq 0 -or $requiredChecks -contains $null -or $requiredChecks -contains '') {
    throw "validation manifest has no valid requiredChecks"
}

$restoreArguments = @('restore', $solution, '--configfile', $nugetConfig)
if ($SkipNuGetAudit) {
    $restoreArguments += '-p:NuGetAudit=false'
}

Invoke-Check -Id 'restore' -Description 'restore' -Action {
    Invoke-Dotnet -Arguments $restoreArguments
}

Invoke-Check -Id 'format' -Description 'format and analyzer verification' -Action {
    Invoke-Dotnet -Arguments @('format', $solution, '--verify-no-changes', '--no-restore', '--verbosity', 'minimal')
}

Invoke-Check -Id 'build-release' -Description 'Release build with warnings as errors' -Action {
    Invoke-Dotnet -Arguments @('build', $solution, '-c', 'Release', '--no-restore')
}

Invoke-Check -Id 'test' -Description 'full Release test run' -Action {
    Invoke-Dotnet -Arguments @('test', $solution, '-c', 'Release', '--no-build', '--no-restore', '--logger', 'console;verbosity=minimal')
}

Invoke-Check -Id 'phase0-matrix' -Description 'promoted Phase 0 rule matrix' -Action {
    Invoke-Dotnet -Arguments @('run', '--project', $experimentProject, '-c', 'Release', '--no-build', '--no-restore', '--', '--matrix')
}

Invoke-Check -Id 'canonical-determinism' -Description 'canonical bytes and encoding determinism' -Action {
    $runA = Join-Path $canonicalRoot 'run-a'
    $runB = Join-Path $canonicalRoot 'run-b'
    Remove-Item -LiteralPath $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue

    Invoke-Dotnet -Arguments @('run', '--project', $experimentProject, '-c', 'Release', '--no-build', '--no-restore', '--', '--canonical', $runA)
    Invoke-Dotnet -Arguments @('run', '--project', $experimentProject, '-c', 'Release', '--no-build', '--no-restore', '--', '--canonical', $runB)

    $fileA = Join-Path $runA $canonicalFileName
    $fileB = Join-Path $runB $canonicalFileName
    if (-not (Test-Path -LiteralPath $fileA) -or -not (Test-Path -LiteralPath $fileB)) {
        throw 'canonical document was not written by both independent processes'
    }

    $bytesA = [System.IO.File]::ReadAllBytes($fileA)
    $bytesB = [System.IO.File]::ReadAllBytes($fileB)
    $identical = [System.Linq.Enumerable]::SequenceEqual($bytesA, $bytesB)
    $textA = [System.Text.Encoding]::UTF8.GetString($bytesA)
    $document = $textA | ConvertFrom-Json
    $hasBom = $bytesA.Length -ge 3 -and $bytesA[0] -eq 0xEF -and $bytesA[1] -eq 0xBB -and $bytesA[2] -eq 0xBF
    $hasCr = $bytesA -contains [byte][char]"`r"
    $hasFinalLf = $bytesA.Length -gt 0 -and $bytesA[$bytesA.Length - 1] -eq [byte][char]"`n"

    Write-Host "canonical bytes (run a): $($bytesA.Length)"
    Write-Host "canonical bytes (run b): $($bytesB.Length)"
    Write-Host "canonical sha256 (run a): $(Get-Sha256 -Bytes $bytesA)"
    Write-Host "canonical sha256 (run b): $(Get-Sha256 -Bytes $bytesB)"
    Write-Host "canonical formatVersion: $($document.formatVersion)"
    Write-Host "canonical identical bytes: $identical"

    if (-not $identical -or $document.formatVersion -ne 1 -or $hasBom -or $hasCr -or -not $hasFinalLf) {
        throw 'canonical determinism or encoding contract failed'
    }

    Remove-Item -LiteralPath $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue
}

Invoke-Check -Id 'package' -Description 'packable product artifact' -Action {
    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
    Invoke-Dotnet -Arguments @('pack', $productProject, '-c', 'Release', '--no-build', '--no-restore', '-o', $packageOutput)
}

Write-Section 'Manifest'
$null = $executed.Add('manifest')
$missingChecks = @($requiredChecks | Where-Object { -not $executed.Contains([string]$_) })
if ($missingChecks.Count -gt 0) {
    $failures++
    Write-Host "FAILED: required validation checks were not executed: $($missingChecks -join ', ')"
}
else {
    Write-Host "manifest passed: executed $($executed.Count) required checks"
}

Write-Section 'Result'
if ($failures -gt 0) {
    Write-Host "validation failed: $failures check(s) failed"
    exit 1
}

Write-Host "validation passed: $($executed.Count) checks executed"
exit 0
