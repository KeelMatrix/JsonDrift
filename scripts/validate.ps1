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
$expectedPackageId = 'KeelMatrix.JsonDrift'
$expectedPackageVersion = '0.1.0'
$requiredIconPath = Join-Path $repoRoot 'icon.png'
$consumerSmokeScript = Join-Path $PSScriptRoot 'consumer-smoke.ps1'
$documentationLinks = @(
    'https://github.com/KeelMatrix/JsonDrift/blob/main/README.md',
    'https://github.com/KeelMatrix/JsonDrift/blob/main/docs/compatibility-rules.md',
    'https://github.com/KeelMatrix/JsonDrift/blob/main/docs/initial-release-scope.md',
    'https://github.com/KeelMatrix/JsonDrift/blob/main/SECURITY.md',
    'https://github.com/KeelMatrix/JsonDrift/blob/main/PRIVACY.md'
)
$executed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$failures = 0

# Repository development and validation must never count as production demand.
$env:KEELMATRIX_NO_TELEMETRY = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DO_NOT_TRACK = '1'

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
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        $stopwatch.Stop()
        Write-Host ("duration: {0:F3}s" -f $stopwatch.Elapsed.TotalSeconds)
        Write-Host "passed: $Description"
    }
    catch {
        $stopwatch.Stop()
        Write-Host ("duration: {0:F3}s" -f $stopwatch.Elapsed.TotalSeconds)
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

function Get-ZipEntryBytes {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) {
        throw "package is missing required entry: $Name"
    }

    $stream = $entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        return ,$memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $stream.Dispose()
    }
}

function Get-RequiredXmlText {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlNode]$Parent,
        [Parameter(Mandatory = $true)][string]$LocalName,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $nodes = @($Parent.SelectNodes("*[local-name()='$LocalName']"))
    if ($nodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$nodes[0].InnerText)) {
        throw "$Description is missing or ambiguous"
    }

    return [string]$nodes[0].InnerText
}

function Test-TruthyEnvironmentValue {
    param([string]$Value)
    return $null -ne $Value -and $Value.Trim().ToLowerInvariant() -in @('1', 'true', 'yes', 'y', 'on')
}

function Assert-RepositoryTelemetryDisabled {
    $keys = @('KEELMATRIX_NO_TELEMETRY', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DO_NOT_TRACK')
    $enabled = @($keys | Where-Object { -not (Test-TruthyEnvironmentValue ([Environment]::GetEnvironmentVariable($_))) })
    if ($enabled.Count -gt 0) {
        throw "repository validation telemetry opt-out is not active for: $($enabled -join ', ')"
    }

    Write-Host "telemetry disabled: $($keys -join ', ')"
}

function Assert-PngIcon {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "required founder-owned icon is absent: $Path"
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -gt 200KB) {
        throw "icon exceeds the 200 KB limit: $Path"
    }

    $signature = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
    if ($bytes.Length -lt 24 -or -not [System.Linq.Enumerable]::SequenceEqual([byte[]]$bytes[0..7], $signature)) {
        throw "icon is not a PNG with a readable IHDR: $Path"
    }

    [uint32]$width = (([uint32]$bytes[16] -shl 24) -bor ([uint32]$bytes[17] -shl 16) -bor ([uint32]$bytes[18] -shl 8) -bor [uint32]$bytes[19])
    [uint32]$height = (([uint32]$bytes[20] -shl 24) -bor ([uint32]$bytes[21] -shl 16) -bor ([uint32]$bytes[22] -shl 8) -bor [uint32]$bytes[23])
    if ($width -ne 512 -or $height -ne 512) {
        throw "icon must be exactly 512x512 pixels: $Path (actual ${width}x${height})"
    }
}

function Assert-PackageArtifact {
    param([Parameter(Mandatory = $true)][string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $expectedFixedEntries = @(
            '[Content_Types].xml',
            '_rels/.rels',
            "$expectedPackageId.nuspec",
            'README.md',
            'LICENSE',
            'icon.png',
            "lib/net8.0/$expectedPackageId.dll",
            "lib/net8.0/$expectedPackageId.xml"
        ) | Sort-Object
        $actualEntries = @($archive.Entries | ForEach-Object FullName | Sort-Object)
        # This is a deny-by-default packaging gate. A new SDK-generated entry form must be
        # reviewed and allowlisted rather than silently accepted as an arbitrary .psmdcp file.
        $corePropertiesEntries = @($actualEntries | Where-Object { $_ -cmatch '^package/services/metadata/core-properties/(?:[0-9a-f]{32}|nuget)\.psmdcp$' })
        $unexpectedEntries = @($actualEntries | Where-Object { $_ -cnotin $expectedFixedEntries -and $_ -cnotin $corePropertiesEntries })
        if ($corePropertiesEntries.Count -ne 1 -or $unexpectedEntries.Count -ne 0 -or $actualEntries.Count -ne ($expectedFixedEntries.Count + 1)) {
            throw "package entries differ from the explicit intended artifact set. Expected fixed entries: $($expectedFixedEntries -join ', '); expected one generated core-properties entry; actual: $($actualEntries -join ', ')"
        }

        Write-Host "package entries: $($actualEntries -join ', ')"

        $packedReadme = [System.Text.Encoding]::UTF8.GetString((Get-ZipEntryBytes -Archive $archive -Name 'README.md'))
        $missingDocumentationLinks = @($documentationLinks | Where-Object {
                $packedReadme.IndexOf([string]$_, [System.StringComparison]::Ordinal) -lt 0
            })
        if ($missingDocumentationLinks.Count -gt 0) {
            throw "packed README is missing required deeper-documentation links: $($missingDocumentationLinks -join ', ')"
        }

        Write-Host "packed README deep-documentation links: $($documentationLinks.Count) verified"

        $nuspecBytes = Get-ZipEntryBytes -Archive $archive -Name "$expectedPackageId.nuspec"
        $nuspec = [System.Xml.XmlDocument]::new()
        $nuspecStream = [System.IO.MemoryStream]::new($nuspecBytes)
        try {
            $nuspec.Load($nuspecStream)
        }
        finally {
            $nuspecStream.Dispose()
        }
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($null -eq $metadata) {
            throw 'package nuspec is missing metadata'
        }

        $nuspecId = Get-RequiredXmlText -Parent $metadata -LocalName 'id' -Description 'package nuspec id'
        $nuspecVersion = Get-RequiredXmlText -Parent $metadata -LocalName 'version' -Description 'package nuspec version'
        $nuspecAuthors = Get-RequiredXmlText -Parent $metadata -LocalName 'authors' -Description 'package nuspec authors'
        $nuspecDescription = Get-RequiredXmlText -Parent $metadata -LocalName 'description' -Description 'package nuspec description'
        $nuspecReadme = Get-RequiredXmlText -Parent $metadata -LocalName 'readme' -Description 'package nuspec README'
        $nuspecIcon = Get-RequiredXmlText -Parent $metadata -LocalName 'icon' -Description 'package nuspec icon'
        if ($nuspecId -cne $expectedPackageId -or
            $nuspecVersion -cne $expectedPackageVersion -or
            $nuspecReadme -cne 'README.md' -or
            $nuspecIcon -cne 'icon.png') {
            throw 'package identity, version, README, or icon metadata is incorrect'
        }

        $corePropertiesBytes = Get-ZipEntryBytes -Archive $archive -Name $corePropertiesEntries[0]
        $coreProperties = [System.Xml.XmlDocument]::new()
        $corePropertiesStream = [System.IO.MemoryStream]::new($corePropertiesBytes)
        try {
            $coreProperties.Load($corePropertiesStream)
        }
        finally {
            $corePropertiesStream.Dispose()
        }

        $corePropertiesRoot = $coreProperties.SelectSingleNode("/*[local-name()='coreProperties']")
        if ($null -eq $corePropertiesRoot) {
            throw "core-properties entry '$($corePropertiesEntries[0])' is missing a coreProperties root"
        }

        $coreCreator = Get-RequiredXmlText -Parent $corePropertiesRoot -LocalName 'creator' -Description 'core-properties creator'
        $coreDescription = Get-RequiredXmlText -Parent $corePropertiesRoot -LocalName 'description' -Description 'core-properties description'
        $coreIdentifier = Get-RequiredXmlText -Parent $corePropertiesRoot -LocalName 'identifier' -Description 'core-properties identifier'
        $coreVersion = Get-RequiredXmlText -Parent $corePropertiesRoot -LocalName 'version' -Description 'core-properties version'
        if ($coreCreator -cne $nuspecAuthors -or
            $coreDescription -cne $nuspecDescription -or
            $coreIdentifier -cne $expectedPackageId -or
            $coreIdentifier -cne $nuspecId -or
            $coreVersion -cne $expectedPackageVersion -or
            $coreVersion -cne $nuspecVersion) {
            throw "core-properties content is inconsistent with package identity or nuspec metadata: $($corePropertiesEntries[0])"
        }

        Write-Host "core-properties: $($corePropertiesEntries[0]) (identity, version, authors, description verified)"

        $license = $metadata.SelectSingleNode("*[local-name()='license']")
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $license -or $license.GetAttribute('type') -cne 'expression' -or $license.InnerText -cne 'MIT') {
            throw 'package license metadata is missing or incorrect'
        }

        if ($null -eq $repository -or $repository.GetAttribute('type') -cne 'git' -or
            $repository.GetAttribute('url') -cne 'https://github.com/KeelMatrix/JsonDrift') {
            throw 'package repository metadata is missing or incorrect'
        }

        $dependencyGroups = @($metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']"))
        if ($dependencyGroups.Count -ne 1 -or $dependencyGroups[0].GetAttribute('targetFramework') -cne 'net8.0') {
            throw 'package runtime dependency groups are not exactly the intended net8.0 group'
        }

        $runtimeDependencies = @($dependencyGroups[0].SelectNodes("*[local-name()='dependency']") | ForEach-Object {
                "$($_.GetAttribute('id'))|$($_.GetAttribute('version'))"
            } | Sort-Object)
        $expectedRuntimeDependencies = @(
            'KeelMatrix.Telemetry|[0.1.0]',
            'System.Text.Json|10.0.12'
        ) | Sort-Object
        if (-not [System.Linq.Enumerable]::SequenceEqual([string[]]$runtimeDependencies, [string[]]$expectedRuntimeDependencies)) {
            throw "runtime dependency graph is incorrect. Expected: $($expectedRuntimeDependencies -join ', '); actual: $($runtimeDependencies -join ', ')"
        }

        Write-Host "runtime dependencies: $($runtimeDependencies -join ', ')"

        $physicalIcon = [System.IO.File]::ReadAllBytes($requiredIconPath)
        $packedIcon = Get-ZipEntryBytes -Archive $archive -Name 'icon.png'
        $physicalIconHash = Get-Sha256 -Bytes $physicalIcon
        $packedIconHash = Get-Sha256 -Bytes $packedIcon
        Write-Host "icon sha256 (repository): $physicalIconHash"
        Write-Host "icon sha256 (packed):     $packedIconHash"
        if ($physicalIconHash -cne $packedIconHash -or -not [System.Linq.Enumerable]::SequenceEqual($physicalIcon, $packedIcon)) {
            throw 'packed icon bytes do not match the required repository icon'
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Set-ZipEntryBytes {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][byte[]]$Bytes
    )

    $existing = $Archive.GetEntry($Name)
    if ($null -ne $existing) {
        $existing.Delete()
    }

    $entry = $Archive.CreateEntry($Name)
    $stream = $entry.Open()
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-PackageGateRejectsMutation {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$CaseName,
        [Parameter(Mandatory = $true)][scriptblock]$Mutate
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) "jsondrift-package-gate-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $copy = Join-Path $root "$CaseName.nupkg"
    try {
        Copy-Item -LiteralPath $PackagePath -Destination $copy
        $archive = [System.IO.Compression.ZipFile]::Open($copy, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            & $Mutate $archive
        }
        finally {
            $archive.Dispose()
        }

        $rejected = $false
        try {
            Assert-PackageArtifact -PackagePath $copy
        }
        catch {
            $rejected = $true
            Write-Host "package-gate regression rejected: $CaseName"
            Write-Host "  $($_.Exception.Message)"
        }

        if (-not $rejected) {
            throw "package-gate regression was accepted unexpectedly: $CaseName"
        }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-PackageGateFailClosed {
    param([Parameter(Mandatory = $true)][string]$PackagePath)

    $corePathPrefix = 'package/services/metadata/core-properties/'
    $coreNamePattern = '^package/services/metadata/core-properties/(?:[0-9a-f]{32}|nuget)\.psmdcp$'

    Assert-PackageGateRejectsMutation -PackagePath $PackagePath -CaseName 'missing-core-properties' -Mutate {
        param($archive)
        $entry = @($archive.Entries | Where-Object { $_.FullName -cmatch $coreNamePattern })
        if ($entry.Count -ne 1) {
            throw 'regression setup could not find the allowlisted core-properties entry'
        }
        $entry[0].Delete()
    }

    Assert-PackageGateRejectsMutation -PackagePath $PackagePath -CaseName 'corrupted-core-properties' -Mutate {
        param($archive)
        $entry = @($archive.Entries | Where-Object { $_.FullName -cmatch $coreNamePattern })
        $bytes = Get-ZipEntryBytes -Archive $archive -Name $entry[0].FullName
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        $corrupted = $text -replace '<version>[^<]+</version>', '<version>0.1.1</version>'
        Set-ZipEntryBytes -Archive $archive -Name $entry[0].FullName -Bytes ([System.Text.Encoding]::UTF8.GetBytes($corrupted))
    }

    Assert-PackageGateRejectsMutation -PackagePath $PackagePath -CaseName 'unexpected-extra-file' -Mutate {
        param($archive)
        Set-ZipEntryBytes -Archive $archive -Name ($corePathPrefix + 'unexpected.txt') -Bytes ([System.Text.Encoding]::UTF8.GetBytes('unexpected'))
    }

    Assert-PackageGateRejectsMutation -PackagePath $PackagePath -CaseName 'other-psmdcp' -Mutate {
        param($archive)
        $entry = @($archive.Entries | Where-Object { $_.FullName -cmatch $coreNamePattern })
        if ($entry.Count -ne 1) {
            throw 'regression setup could not find the allowlisted core-properties entry'
        }
        $bytes = Get-ZipEntryBytes -Archive $archive -Name $entry[0].FullName
        $entry[0].Delete()
        Set-ZipEntryBytes -Archive $archive -Name ($corePathPrefix + 'other.psmdcp') -Bytes $bytes
    }
}

if (-not (Test-Path -LiteralPath $solution)) {
    throw "solution not found: $solution"
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "validation manifest not found: $manifestPath"
}

Invoke-Check -Id 'telemetry-disabled' -Description 'repository validation telemetry opt-out' -Action {
    Assert-RepositoryTelemetryDisabled
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
    Assert-PngIcon -Path $requiredIconPath
    Remove-Item -LiteralPath $packageOutput -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
    Invoke-Dotnet -Arguments @('pack', $productProject, '-c', 'Release', '--no-build', '--no-restore', '-o', $packageOutput)

    $packages = @(Get-ChildItem -LiteralPath $packageOutput -File -Filter '*.nupkg' | Where-Object { $_.Name -notlike '*.symbols.nupkg' })
    $symbols = @(Get-ChildItem -LiteralPath $packageOutput -File -Filter '*.snupkg')
    if ($packages.Count -ne 1 -or $packages[0].Name -cne "$expectedPackageId.$expectedPackageVersion.nupkg") {
        throw "expected exactly one product package named $expectedPackageId.$expectedPackageVersion.nupkg"
    }

    if ($symbols.Count -ne 1 -or $symbols[0].Name -cne "$expectedPackageId.$expectedPackageVersion.snupkg") {
        throw "expected exactly one symbol package named $expectedPackageId.$expectedPackageVersion.snupkg"
    }

    Assert-PackageArtifact -PackagePath $packages[0].FullName
    $script:productPackagePath = $packages[0].FullName
    Write-Host "package: $($packages[0].FullName)"
    Write-Host "symbols: $($symbols[0].FullName)"
}

Invoke-Check -Id 'package-gate-regressions' -Description 'package gate fail-closed regression cases' -Action {
    Assert-PackageGateFailClosed -PackagePath $script:productPackagePath
}

Invoke-Check -Id 'package-consumer' -Description 'isolated package-consumer smoke' -Action {
    if (-not (Test-Path -LiteralPath $consumerSmokeScript -PathType Leaf)) {
        throw "consumer smoke script not found: $consumerSmokeScript"
    }

    & pwsh -NoProfile -File $consumerSmokeScript -PackagePath $script:productPackagePath
    if ($LASTEXITCODE -ne 0) {
        throw "consumer smoke exited with code $LASTEXITCODE"
    }
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
