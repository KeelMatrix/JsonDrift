#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = Get-Item -LiteralPath $PackagePath -ErrorAction Stop
if ($package.Name -notmatch '^KeelMatrix\.JsonDrift\.(?<version>\d+\.\d+\.\d+)\.nupkg$') {
    throw "first-success consumer requires a .nupkg: $PackagePath"
}

$packageVersion = $Matches.version
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$rootReadme = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'README.md'))
$packageReadme = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src/KeelMatrix.JsonDrift/README.md'))
$examplePattern = '(?s)<!-- BEGIN:FIRST-SUCCESS-EXAMPLE -->\s*```csharp\r?\n(?<program>.*?)\r?\n```\s*<!-- END:FIRST-SUCCESS-EXAMPLE -->'

function Get-DocumentedProgram {
    param([Parameter(Mandatory = $true)][string]$Text)

    $match = [regex]::Match($Text, $examplePattern)
    if (-not $match.Success) {
        throw 'the complete first-success example markers were not found'
    }

    return $match.Groups['program'].Value
}

$rootProgram = Get-DocumentedProgram -Text $rootReadme
$packageProgram = Get-DocumentedProgram -Text $packageReadme
if ($rootProgram -cne $packageProgram) {
    throw 'root and packed README first-success examples differ'
}

Add-Type -AssemblyName System.IO.Compression
$packageArchive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $readmeEntry = $packageArchive.GetEntry('README.md')
    if ($null -eq $readmeEntry) {
        throw 'the built package is missing README.md'
    }

    $reader = [IO.StreamReader]::new($readmeEntry.Open(), [Text.UTF8Encoding]::new($false))
    try {
        $packedReadme = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $packageArchive.Dispose()
}

if ($packageProgram -cne (Get-DocumentedProgram -Text $packedReadme)) {
    throw 'the built package README first-success example differs from the repository package README'
}

$root = Join-Path ([IO.Path]::GetTempPath()) "jsondrift-first-success-$([Guid]::NewGuid().ToString('N'))"
$project = Join-Path $root 'JsonDriftExample'
$packages = Join-Path $root 'packages'
$feed = Join-Path $root 'feed'

try {
    New-Item -ItemType Directory -Path $project, $packages, $feed -Force | Out-Null
    Copy-Item -LiteralPath $package.FullName -Destination $feed

    $escapedFeed = [System.Security.SecurityElement]::Escape($feed)
    $projectFile = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="KeelMatrix.JsonDrift" Version="$packageVersion" />
  </ItemGroup>
</Project>
"@

    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-package-under-test" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-package-under-test">
      <package pattern="KeelMatrix.JsonDrift" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@

    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $project 'JsonDriftExample.csproj'), $projectFile, $utf8)
    [IO.File]::WriteAllText((Join-Path $project 'NuGet.config'), $nugetConfig, $utf8)
    [IO.File]::WriteAllText((Join-Path $project 'Program.cs'), $rootProgram, $utf8)

    $env:NUGET_PACKAGES = $packages
    $env:KEELMATRIX_NO_TELEMETRY = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DO_NOT_TRACK = '1'

    & dotnet restore (Join-Path $project 'JsonDriftExample.csproj') --configfile (Join-Path $project 'NuGet.config') --force-evaluate --no-cache
    if ($LASTEXITCODE -ne 0) {
        throw "first-success consumer restore exited with code $LASTEXITCODE"
    }

    function Invoke-ExampleCommand {
        param(
            [Parameter(Mandatory = $true)][string]$Command,
            [Parameter(Mandatory = $true)][string[]]$ExpectedOutput
        )

        Push-Location $project
        try {
            $output = @(& dotnet run --project (Join-Path $project 'JsonDriftExample.csproj') -c Release --no-restore -- $Command 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        $output | ForEach-Object { Write-Host $_ }
        if ($exitCode -ne 0) {
            throw "first-success '$Command' exited with code $exitCode"
        }

        $joined = $output -join [Environment]::NewLine
        foreach ($expected in $ExpectedOutput) {
            if ($joined -notmatch [regex]::Escape($expected)) {
                throw "first-success '$Command' did not emit '$expected'"
            }
        }
    }

    Write-Host 'first-success documented example: baseline creation'
    Invoke-ExampleCommand -Command 'create-baseline' -ExpectedOutput 'baseline created: contracts/order-event.json'
    Write-Host 'first-success documented example: compatible change'
    Invoke-ExampleCommand -Command 'compatible' -ExpectedOutput 'compatible change: Compatible'
    Write-Host 'first-success documented example: rejected change'
    Invoke-ExampleCommand -Command 'breaking' -ExpectedOutput @(
        'rejected change: Incompatible',
        'AssertCompatible rejected the change.'
    )
    Write-Output 'documented first-success package example passed: baseline, compatible change, rejected change'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
