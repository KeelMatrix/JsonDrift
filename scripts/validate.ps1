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
    [switch]$SkipNuGetAudit,
    [string]$ExpectedPackageVersion
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
$versionPropsPath = Join-Path $repoRoot 'Directory.Build.props'
$expectedPackageVersion = if ([string]::IsNullOrWhiteSpace($ExpectedPackageVersion)) {
    $versionMatch = [regex]::Match([IO.File]::ReadAllText($versionPropsPath), '<Version>(?<version>[^<]+)</Version>')
    if (-not $versionMatch.Success) {
        throw "could not derive package version from $versionPropsPath"
    }
    $versionMatch.Groups['version'].Value
}
else {
    $ExpectedPackageVersion.Trim()
}
$expectedRepositoryCommit = (& git -C $repoRoot rev-parse HEAD 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $expectedRepositoryCommit -notmatch '^[0-9a-fA-F]{40,64}$') {
    throw 'could not resolve the repository commit for package provenance validation'
}
$requiredIconPath = Join-Path $repoRoot 'icon.png'
$consumerSmokeScript = Join-Path $PSScriptRoot 'consumer-smoke.ps1'
$securityAuditTimeoutSeconds = 120
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
    $output = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    $auditDiagnostics = @($output | Where-Object {
            ([string]$_) -match '(?i)\bNU190[0-4]\b'
        })
    if ($auditDiagnostics.Count -gt 0) {
        throw 'dotnet command emitted a NuGet vulnerability-audit diagnostic; advisory data was unavailable or a vulnerable package was reported'
    }
    if ($exitCode -ne 0) {
        throw "dotnet exited with code $exitCode"
    }
}

function Invoke-DotnetCapture {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $argumentListProperty = $startInfo.PSObject.Properties['ArgumentList']
    if ($null -ne $argumentListProperty) {
        foreach ($argument in $Arguments) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
    }
    else {
        $startInfo.Arguments = ($Arguments | ForEach-Object {
                $escaped = $_ -replace '(\\*)"', '$1$1\\"'
                $escaped = $escaped -replace '(\\+)$', '$1$1'
                '"' + $escaped + '"'
            }) -join ' '
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'process did not start'
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
            }
            catch {
                Write-Host "security audit process cleanup failed: $($_.Exception.Message)"
            }

            throw "process exceeded the $TimeoutSeconds second timeout"
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdoutTask.GetAwaiter().GetResult()
            StandardError = $stderrTask.GetAwaiter().GetResult()
        }
    }
    catch {
        throw "security audit could not execute: $($_.Exception.Message)"
    }
    finally {
        $process.Dispose()
    }
}

function Find-JsonVulnerabilities {
    param(
        [Parameter(Mandatory = $true)]$Node,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Findings
    )

    if ($null -eq $Node) {
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($child in $Node) {
            Find-JsonVulnerabilities -Node $child -Findings $Findings
        }
        return
    }

    foreach ($property in $Node.PSObject.Properties) {
        if ($property.Name -ieq 'vulnerabilities') {
            foreach ($finding in @($property.Value)) {
                if ($null -ne $finding) {
                    $Findings.Add($finding)
                }
            }
        }
        else {
            Find-JsonVulnerabilities -Node $property.Value -Findings $Findings
        }
    }
}

function Invoke-SecurityAudit {
    $arguments = @(
        'list',
        $solution,
        'package',
        '--vulnerable',
        '--include-transitive',
        '--format',
        'json',
        '--output-version',
        '1',
        '--configfile',
        $nugetConfig
    )

    Write-Host "dotnet $($arguments -join ' ')"
    Write-Host "security audit timeout: $securityAuditTimeoutSeconds seconds"
    $result = Invoke-DotnetCapture -Arguments $arguments -TimeoutSeconds $securityAuditTimeoutSeconds
    $stdout = [string]$result.StandardOutput
    $stderr = [string]$result.StandardError

    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        $stdout.TrimEnd("`r", "`n").Split(@("`r`n", "`n", "`r"), [System.StringSplitOptions]::None) | ForEach-Object {
            Write-Host $_
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        $stderr.TrimEnd("`r", "`n").Split(@("`r`n", "`n", "`r"), [System.StringSplitOptions]::None) | ForEach-Object {
            Write-Host $_
        }
    }

    $diagnostics = "$stdout`n$stderr"
    if ($diagnostics -match '(?im)\bNU190[0-4]\b|\bNU130[12]\b|unable to load the service index|failed to (?:load|retrieve) (?:the )?(?:vulnerability|advisory)|(?:vulnerability|advisory) (?:service|data).*(?:unavailable|unreachable|failed|error)') {
        throw 'security audit failed closed: advisory data was unavailable or an audit diagnostic was reported'
    }

    if ($result.ExitCode -ne 0) {
        throw "security audit failed closed: command exited with code $($result.ExitCode); audit result is unperformed"
    }

    if ([string]::IsNullOrWhiteSpace($stdout)) {
        throw 'security audit failed closed: command produced no machine-readable result'
    }

    try {
        $audit = $stdout | ConvertFrom-Json
    }
    catch {
        throw "security audit failed closed: command did not produce valid JSON: $($_.Exception.Message)"
    }

    if ($null -eq $audit -or $audit.version -ne 1 -or $null -eq $audit.projects -or @($audit.projects).Count -eq 0) {
        throw 'security audit failed closed: JSON result did not contain the expected version-1 project set'
    }

    $findings = [System.Collections.Generic.List[object]]::new()
    Find-JsonVulnerabilities -Node $audit -Findings $findings
    if ($findings.Count -gt 0) {
        throw "security audit failed: $($findings.Count) vulnerable package advisory result(s) reported"
    }

    Write-Host 'security audit executed: true'
    Write-Host "security audit result: clean; no vulnerable direct or transitive packages reported across $(@($audit.projects).Count) project(s)"
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
        throw "required package icon is absent: $Path"
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
            $repository.GetAttribute('url') -cne 'https://github.com/KeelMatrix/JsonDrift' -or
            $repository.GetAttribute('commit') -cne $expectedRepositoryCommit) {
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

function Get-PortablePdbIdentity {
    param(
        [Parameter(Mandatory = $true)][byte[]]$AssemblyBytes,
        [Parameter(Mandatory = $true)][byte[]]$PdbBytes
    )

    Add-Type -AssemblyName System.Reflection.Metadata
    $assemblyStream = [System.IO.MemoryStream]::new($AssemblyBytes, $false)
    $pdbStream = [System.IO.MemoryStream]::new($PdbBytes, $false)
    $peReader = $null
    $pdbProvider = $null
    try {
        $peReader = [System.Reflection.PortableExecutable.PEReader]::new($assemblyStream)
        $codeViewEntries = @($peReader.ReadDebugDirectory() | Where-Object {
                $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView
            })
        if ($codeViewEntries.Count -ne 1) {
            throw "shipped assembly must contain exactly one CodeView debug entry; found $($codeViewEntries.Count)"
        }

        $codeView = $peReader.ReadCodeViewDebugDirectoryData($codeViewEntries[0])
        $pdbProvider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        $id = $pdbProvider.GetMetadataReader().DebugMetadataHeader.Id
        if ($id.Length -lt 16) {
            throw 'portable PDB debug metadata identity is too short'
        }

        $idBytes = [byte[]]::new(16)
        for ($index = 0; $index -lt 16; $index++) {
            $idBytes[$index] = $id[$index]
        }

        $pdbGuid = [Guid]::new($idBytes)
        if ($pdbGuid -ne $codeView.Guid) {
            throw "portable PDB identity $pdbGuid does not match shipped assembly CodeView identity $($codeView.Guid)"
        }

        return [pscustomobject]@{
            CodeViewGuid = [string]$codeView.Guid
            CodeViewPath = [string]$codeView.Path
        }
    }
    finally {
        if ($null -ne $pdbProvider) { $pdbProvider.Dispose() }
        if ($null -ne $peReader) { $peReader.Dispose() }
        $pdbStream.Dispose()
        $assemblyStream.Dispose()
    }
}

function Assert-SymbolArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$SymbolsPackagePath,
        [Parameter(Mandatory = $true)][string]$PackagePath
    )

    if (-not (Test-Path -LiteralPath $SymbolsPackagePath -PathType Leaf)) {
        throw "symbol package is missing: $SymbolsPackagePath"
    }
    if ([IO.Path]::GetFileName($SymbolsPackagePath) -cne "$expectedPackageId.$expectedPackageVersion.snupkg") {
        throw 'symbol package filename does not match the intended package identity and version'
    }

    Add-Type -AssemblyName System.IO.Compression
    $symbolsArchive = $null
    try {
        $symbolsArchive = [System.IO.Compression.ZipFile]::OpenRead($SymbolsPackagePath)
        $fixedEntries = @(
            '[Content_Types].xml',
            '_rels/.rels',
            "$expectedPackageId.nuspec",
            "lib/net8.0/$expectedPackageId.pdb"
        ) | Sort-Object
        $actualEntries = @($symbolsArchive.Entries | ForEach-Object FullName | Sort-Object)
        $corePropertiesEntries = @($actualEntries | Where-Object { $_ -cmatch '^package/services/metadata/core-properties/(?:[0-9a-f]{32}|nuget)\.psmdcp$' })
        $unexpectedEntries = @($actualEntries | Where-Object { $_ -cnotin $fixedEntries -and $_ -cnotin $corePropertiesEntries })
        if ($corePropertiesEntries.Count -ne 1 -or $unexpectedEntries.Count -ne 0 -or
            $actualEntries.Count -ne ($fixedEntries.Count + 1)) {
            throw "symbol archive entries differ from the explicit intended set; actual: $($actualEntries -join ', ')"
        }

        $nuspec = [System.Xml.XmlDocument]::new()
        $nuspecStream = [System.IO.MemoryStream]::new((Get-ZipEntryBytes -Archive $symbolsArchive -Name "$expectedPackageId.nuspec"))
        try { $nuspec.Load($nuspecStream) } finally { $nuspecStream.Dispose() }
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($null -eq $metadata) { throw 'symbol package nuspec is missing metadata' }

        $symbolId = Get-RequiredXmlText -Parent $metadata -LocalName 'id' -Description 'symbol package id'
        $symbolVersion = Get-RequiredXmlText -Parent $metadata -LocalName 'version' -Description 'symbol package version'
        if ($symbolId -cne $expectedPackageId -or $symbolVersion -cne $expectedPackageVersion) {
            throw 'symbol package nuspec identity or version is incorrect'
        }

        $packageTypes = @($metadata.SelectNodes("*[local-name()='packageTypes']/*[local-name()='packageType']") | ForEach-Object { $_.GetAttribute('name') })
        if ($packageTypes.Count -ne 1 -or $packageTypes[0] -cne 'SymbolsPackage') {
            throw 'symbol package nuspec must declare exactly one SymbolsPackage type'
        }

        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or $repository.GetAttribute('type') -cne 'git' -or
            $repository.GetAttribute('url') -cne 'https://github.com/KeelMatrix/JsonDrift' -or
            $repository.GetAttribute('commit') -cne $expectedRepositoryCommit) {
            throw 'symbol package repository mapping does not identify the reviewed commit'
        }

        $pdbBytes = Get-ZipEntryBytes -Archive $symbolsArchive -Name "lib/net8.0/$expectedPackageId.pdb"
        $pdbText = [System.Text.Encoding]::UTF8.GetString($pdbBytes)
        $sourceLink = "https://raw.githubusercontent.com/KeelMatrix/JsonDrift/$expectedRepositoryCommit/*"
        if (-not $pdbText.Contains($sourceLink, [StringComparison]::Ordinal)) {
            throw "symbol PDB SourceLink does not point at the reviewed commit '$expectedRepositoryCommit'"
        }
        if ($pdbText -match '(?i)(?<![A-Za-z0-9])(?:[A-Z]:[\\/][^\x00"<>\r\n]*|/Users/[^\x00"<>\r\n]*|/home/[^\x00"<>\r\n]*|/root/[^\x00"<>\r\n]*)') {
            throw 'symbol PDB contains a private machine path'
        }

        Add-Type -AssemblyName System.IO.Compression
        $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
        try {
            $assemblyBytes = Get-ZipEntryBytes -Archive $packageArchive -Name "lib/net8.0/$expectedPackageId.dll"
        }
        finally {
            $packageArchive.Dispose()
        }

        $identity = Get-PortablePdbIdentity -AssemblyBytes $assemblyBytes -PdbBytes $pdbBytes
        if (-not $identity.CodeViewPath.EndsWith("/$expectedPackageId.pdb", [StringComparison]::Ordinal) -and
            -not $identity.CodeViewPath.EndsWith("\$expectedPackageId.pdb", [StringComparison]::Ordinal)) {
            throw "shipped assembly CodeView path does not identify $expectedPackageId.pdb"
        }

        Write-Host "symbol entries: $($actualEntries -join ', ')"
        Write-Host "symbol PDB: $expectedPackageId.pdb (SourceLink, reviewed commit, and assembly identity verified)"
    }
    finally {
        if ($null -ne $symbolsArchive) { $symbolsArchive.Dispose() }
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

function Assert-SymbolGateRejectsMutation {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$SymbolsPackagePath,
        [Parameter(Mandatory = $true)][string]$CaseName,
        [Parameter(Mandatory = $true)][scriptblock]$Mutate
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) "jsondrift-symbol-gate-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $copy = Join-Path $root "$CaseName.snupkg"
    try {
        Copy-Item -LiteralPath $SymbolsPackagePath -Destination $copy
        & $Mutate $copy
        $rejected = $false
        try {
            Assert-SymbolArtifact -SymbolsPackagePath $copy -PackagePath $PackagePath
        }
        catch {
            $rejected = $true
            Write-Host "symbol-gate regression rejected: $CaseName"
            Write-Host "  $($_.Exception.Message)"
        }
        if (-not $rejected) {
            throw "symbol-gate regression was accepted unexpectedly: $CaseName"
        }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-SymbolGateFailClosed {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$SymbolsPackagePath
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) "jsondrift-symbol-missing-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $missing = Join-Path $root 'missing.snupkg'
        $missingRejected = $false
        try { Assert-SymbolArtifact -SymbolsPackagePath $missing -PackagePath $PackagePath }
        catch { $missingRejected = $true; Write-Host 'symbol-gate regression rejected: missing-symbol-package' }
        if (-not $missingRejected) { throw 'symbol-gate regression was accepted unexpectedly: missing-symbol-package' }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }

    Assert-SymbolGateRejectsMutation -PackagePath $PackagePath -SymbolsPackagePath $SymbolsPackagePath -CaseName 'corrupt-symbol-package' -Mutate {
        param($path)
        [IO.File]::WriteAllBytes($path, [byte[]](0x6e, 0x6f, 0x74, 0x2d, 0x7a, 0x69, 0x70))
    }

    Assert-SymbolGateRejectsMutation -PackagePath $PackagePath -SymbolsPackagePath $SymbolsPackagePath -CaseName 'unexpected-symbol-content' -Mutate {
        param($path)
        $archive = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.CreateEntry('lib/net8.0/unexpected.pdb')
            $stream = $entry.Open()
            try { $stream.Write([byte[]](0x70, 0x6f, 0x69, 0x73, 0x6f, 0x6e), 0, 6) }
            finally { $stream.Dispose() }
        }
        finally { $archive.Dispose() }
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

Invoke-Check -Id 'release-workflow-contract' -Description 'tag-only release workflow contract' -Action {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-release-workflow.ps1')
    if ($LASTEXITCODE -ne 0) { throw "release workflow contract exited with code $LASTEXITCODE" }
}

Invoke-Check -Id 'changelog-version-regressions' -Description 'finalized, unfinalized, and mismatch release-version regressions' -Action {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-changelog-contract.ps1')
    if ($LASTEXITCODE -ne 0) { throw "changelog contract regressions exited with code $LASTEXITCODE" }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$requiredChecks = @($manifest.requiredChecks)
if ($requiredChecks.Count -eq 0 -or $requiredChecks -contains $null -or $requiredChecks -contains '') {
    throw "validation manifest has no valid requiredChecks"
}

$restoreArguments = @('restore', $solution, '--configfile', $nugetConfig, '--force', '--no-cache')
if ($SkipNuGetAudit) {
    $restoreArguments += '-p:NuGetAudit=false'
}

Invoke-Check -Id 'restore' -Description 'restore' -Action {
    Invoke-Dotnet -Arguments $restoreArguments
}

Invoke-Check -Id 'security-audit' -Description 'direct and transitive NuGet vulnerability audit' -Action {
    Invoke-SecurityAudit
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

    if (-not $identical -or $document.formatVersion -ne 2 -or $hasBom -or $hasCr -or -not $hasFinalLf) {
        throw 'canonical determinism or encoding contract failed'
    }

    Remove-Item -LiteralPath $runA, $runB -Recurse -Force -ErrorAction SilentlyContinue
}

Invoke-Check -Id 'package' -Description 'packable product artifact' -Action {
    Assert-PngIcon -Path $requiredIconPath
    Remove-Item -LiteralPath $packageOutput -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
    Invoke-Dotnet -Arguments @('pack', $productProject, '-c', 'Release', '--no-build', '--no-restore', '--include-symbols', '-p:SymbolPackageFormat=snupkg', '-o', $packageOutput)

    $packages = @(Get-ChildItem -LiteralPath $packageOutput -File -Filter '*.nupkg' | Where-Object { $_.Name -notlike '*.symbols.nupkg' })
    $symbols = @(Get-ChildItem -LiteralPath $packageOutput -File -Filter '*.snupkg')
    if ($packages.Count -ne 1 -or $packages[0].Name -cne "$expectedPackageId.$expectedPackageVersion.nupkg") {
        throw "expected exactly one product package named $expectedPackageId.$expectedPackageVersion.nupkg"
    }

    if ($symbols.Count -ne 1 -or $symbols[0].Name -cne "$expectedPackageId.$expectedPackageVersion.snupkg") {
        throw "expected exactly one symbol package named $expectedPackageId.$expectedPackageVersion.snupkg"
    }

    Assert-PackageArtifact -PackagePath $packages[0].FullName
    Assert-SymbolArtifact -SymbolsPackagePath $symbols[0].FullName -PackagePath $packages[0].FullName
    $script:productPackagePath = $packages[0].FullName
    $script:productSymbolsPath = $symbols[0].FullName
    Write-Host "package: $($packages[0].FullName)"
    Write-Host "symbols: $($symbols[0].FullName)"
}

Invoke-Check -Id 'package-gate-regressions' -Description 'package gate fail-closed regression cases' -Action {
    Assert-PackageGateFailClosed -PackagePath $script:productPackagePath
}

Invoke-Check -Id 'symbol-package-gate-regressions' -Description 'symbol package fail-closed regression cases' -Action {
    Assert-SymbolGateFailClosed -PackagePath $script:productPackagePath -SymbolsPackagePath $script:productSymbolsPath
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

Invoke-Check -Id 'package-consumer-cache-regression' -Description 'poisoned inherited package-cache regression' -Action {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-consumer-smoke.ps1') -PackagePath $script:productPackagePath
    if ($LASTEXITCODE -ne 0) { throw "consumer cache regression exited with code $LASTEXITCODE" }
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
