[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$validator = Join-Path $PSScriptRoot 'validate-release-version.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "jsondrift-release-contract-$([Guid]::NewGuid().ToString('N'))"
$utf8 = [Text.UTF8Encoding]::new($false)

function Write-FixtureFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Content, $utf8)
}

function New-FixtureRepository {
    param([Parameter(Mandatory = $true)][string]$Changelog)

    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'src/KeelMatrix.JsonDrift') -Force | Out-Null
    Write-FixtureFile (Join-Path $fixtureRoot 'Directory.Build.props') @'
<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>
'@
    Write-FixtureFile (Join-Path $fixtureRoot 'src/KeelMatrix.JsonDrift/KeelMatrix.JsonDrift.csproj') '<Project />'
    Write-FixtureFile (Join-Path $fixtureRoot 'README.md') 'dotnet add package KeelMatrix.JsonDrift --version 0.1.0'
    Write-FixtureFile (Join-Path $fixtureRoot 'src/KeelMatrix.JsonDrift/README.md') 'dotnet add package KeelMatrix.JsonDrift --version 0.1.0'
    Write-FixtureFile (Join-Path $fixtureRoot 'CHANGELOG.md') $Changelog
    & git -C $fixtureRoot init --quiet
    & git -C $fixtureRoot config user.email contract@example.invalid
    & git -C $fixtureRoot config user.name Contract
    & git -C $fixtureRoot add .
    & git -C $fixtureRoot commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) {
        throw 'could not commit changelog contract fixture'
    }
    return (& git -C $fixtureRoot rev-parse HEAD).Trim()
}

function Invoke-ContractCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Changelog,
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][bool]$ShouldPass
    )

    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }

    $commit = New-FixtureRepository -Changelog $Changelog
    $output = @(& pwsh -NoProfile -File $validator -ExpectedVersion $ExpectedVersion -ExpectedCommit $commit -RepositoryRoot $fixtureRoot 2>&1)
    $passed = $LASTEXITCODE -eq 0
    if ($passed -ne $ShouldPass) {
        throw "changelog contract regression '$Name' produced exit $LASTEXITCODE; output: $($output -join [Environment]::NewLine)"
    }

    $result = if ($passed) { 'accepted' } else { 'rejected' }
    Write-Output "changelog regression: $Name -> $result"
}

try {
    Invoke-ContractCase -Name 'finalized' -ExpectedVersion '0.1.0' -ShouldPass $true -Changelog @'
# Changelog

## [Unreleased]

## [0.1.0] - 2026-09-21

### Added

- Finalized release notes.
'@

    Invoke-ContractCase -Name 'unfinalized' -ExpectedVersion '0.1.0' -ShouldPass $false -Changelog @'
# Changelog

## [Unreleased]

## [0.1.0] - Planned

- Not yet published.
'@

    Invoke-ContractCase -Name 'impossible-calendar-date' -ExpectedVersion '0.1.0' -ShouldPass $false -Changelog @'
# Changelog

## [Unreleased]

## [0.1.0] - 2026-99-99

### Added

- Finalized release notes.
'@

    Invoke-ContractCase -Name 'disallowed-first-release-category' -ExpectedVersion '0.1.0' -ShouldPass $false -Changelog @'
# Changelog

## [Unreleased]

## [0.1.0] - 2026-09-21

### Fixed

- Finalized release notes.
'@

    Invoke-ContractCase -Name 'version-mismatch' -ExpectedVersion '0.1.1' -ShouldPass $false -Changelog @'
# Changelog

## [Unreleased]

## [0.1.0] - 2026-09-21

### Added

- Finalized release notes.
'@
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Output 'Changelog contract regressions passed.'
