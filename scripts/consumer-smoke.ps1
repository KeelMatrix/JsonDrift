#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$package = Get-Item -LiteralPath $PackagePath -ErrorAction Stop
if ($package.Extension -ne '.nupkg') {
    throw "consumer smoke requires a .nupkg: $PackagePath"
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) "jsondrift-consumer-$([Guid]::NewGuid().ToString('N'))"
$project = Join-Path $root 'Consumer'
$packages = Join-Path $root 'packages'
$feed = Join-Path $root 'feed'
New-Item -ItemType Directory -Path $project, $packages, $feed -Force | Out-Null
Copy-Item -LiteralPath $package.FullName -Destination $feed

$localSource = [System.Security.SecurityElement]::Escape($feed)
$projectFile = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="KeelMatrix.JsonDrift" Version="0.1.0" />
  </ItemGroup>
</Project>
'@

$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-package-under-test" value="$localSource" />
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

$program = @'
using System.Text.Json.Serialization;
using KeelMatrix.JsonDrift;

string baselinePath = Path.Combine(Path.GetTempPath(), "jsondrift-consumer-" + Guid.NewGuid().ToString("N") + ".json");
try
{
JsonBaseline.Create(SmokeContext.Default.OrderEnvelopeV1, baselinePath, overwrite: false);

JsonDriftReport compatible = JsonDrift.Compare(
    SmokeContext.Default.OrderEnvelopeV1,
    baselinePath,
    JsonCompatibility.ReaderBackward);
if (!compatible.IsCompatible)
{
    throw new InvalidOperationException("the packaged quick start did not pass its unchanged comparison");
}

JsonDriftReport breaking = JsonDrift.Compare(
    SmokeContext.Default.OrderEnvelopeV2,
    baselinePath,
    JsonCompatibility.ReaderBackward);
JsonDriftChange change = breaking.Changes.FirstOrDefault()
    ?? throw new InvalidOperationException("the packaged quick start did not report the required change");
if (breaking.Outcome != JsonDriftClassification.Incompatible || string.IsNullOrWhiteSpace(change.Reason))
{
    throw new InvalidOperationException($"the packaged quick start did not report a structured incompatible reason: {change}");
}

try
{
    breaking.AssertCompatible();
    throw new InvalidOperationException("the packaged quick start accepted an incompatible change");
}
catch (JsonDriftCompatibilityException)
{
    Console.WriteLine($"consumer smoke passed: {change.RuleId}: {change.Reason}");
}
}
finally
{
    File.Delete(baselinePath);
}

public sealed class OrderEnvelopeV1
{
    public int Id { get; set; }
}

public sealed class OrderEnvelopeV2
{
    public int Id { get; set; }

    [JsonRequired]
    public string Required { get; set; } = string.Empty;
}

[JsonSerializable(typeof(OrderEnvelopeV1))]
[JsonSerializable(typeof(OrderEnvelopeV2))]
public partial class SmokeContext : JsonSerializerContext
{
}
'@

try {
    Set-Content -LiteralPath (Join-Path $project 'Consumer.csproj') -Value $projectFile -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $project 'NuGet.config') -Value $nugetConfig -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $program -Encoding UTF8

    $env:NUGET_PACKAGES = $packages
    $env:KEELMATRIX_NO_TELEMETRY = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DO_NOT_TRACK = '1'

    & dotnet restore (Join-Path $project 'Consumer.csproj') --configfile (Join-Path $project 'NuGet.config') --force-evaluate --no-cache
    if ($LASTEXITCODE -ne 0) {
        throw "consumer restore exited with code $LASTEXITCODE"
    }

    & dotnet run --project (Join-Path $project 'Consumer.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "consumer run exited with code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
