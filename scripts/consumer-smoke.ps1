#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$package = Get-Item -LiteralPath $PackagePath -ErrorAction Stop
if ($package.Name -notmatch '^KeelMatrix\.JsonDrift\.(?<version>\d+\.\d+\.\d+)\.nupkg$') {
    throw "consumer smoke requires a .nupkg: $PackagePath"
}
$packageVersion = $Matches.version

$root = Join-Path ([System.IO.Path]::GetTempPath()) "jsondrift-consumer-$([Guid]::NewGuid().ToString('N'))"
$project = Join-Path $root 'Consumer'
$inheritedPackages = $env:NUGET_PACKAGES
$packages = Join-Path $root 'packages'
$feed = Join-Path $root 'feed'
New-Item -ItemType Directory -Path $project, $packages, $feed -Force | Out-Null
Copy-Item -LiteralPath $package.FullName -Destination $feed
Write-Host "consumer package cache: isolated ($packages)"
if (-not [string]::IsNullOrWhiteSpace($inheritedPackages)) {
    Write-Host "inherited package cache ignored: $inheritedPackages"
}

$localSource = [System.Security.SecurityElement]::Escape($feed)
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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

RequireRejected(Compare(SmokeContext.Default.Int32, SmokeContext.Default.NullableInt32));
RequireRejected(Compare(SmokeContext.Default.GetterOnlySmoke, SmokeContext.Default.WritableSmoke));
RequireRejected(
    Compare(SmokeContext.Default.HashSetInt32, SmokeContext.Default.ListInt32),
    JsonDriftClassification.Unsupported);

var acceptsIntegers = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
acceptsIntegers.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
var rejectsIntegers = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
rejectsIntegers.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
RequireRejected(Compare(
    rejectsIntegers.GetTypeInfo(typeof(SmokeState)),
    acceptsIntegers.GetTypeInfo(typeof(SmokeState))));

JsonDriftReport independent = Compare(SmokeContext.Default.IncludedRequiredSmoke, SmokeContext.Default.IgnoredSmoke);
RequireRejected(independent);
if (!independent.Changes.Any(change => change.RuleId == "R09.ignore.member-included") ||
    !independent.Changes.Any(change => change.RuleId == "R04.requiredness.optional-to-required"))
{
    throw new InvalidOperationException("the packaged comparison suppressed an independent member constraint");
}

RequireCompatible(Compare(SmokeContext.Default.Int64, SmokeContext.Default.Int32));
RequireCompatible(Compare(SmokeContext.Default.Int32Array, SmokeContext.Default.ListInt32));
RequireCompatible(Compare(SmokeContext.Default.OrderEnvelopeOptional, SmokeContext.Default.OrderEnvelopeV1));
RequireCompatible(Compare(SmokeContext.Default.RecursiveSmoke, SmokeContext.Default.RecursiveSmoke));

JsonObject malformed = JsonNode.Parse(JsonDrift.Extract(SmokeContext.Default.RecursiveSmoke).CanonicalJson)!.AsObject();
malformed["root"] = new JsonObject
{
    ["typeName"] = typeof(RecursiveSmoke).FullName,
    ["kind"] = "reference",
    ["acceptsNull"] = false,
    ["reachedBy"] = "resolver-chain",
    ["path"] = "root",
    ["rule"] = "supported.reference",
    ["supported"] = true,
    ["declaredAttributes"] = new JsonArray(),
    ["reference"] = "root",
};
string malformedPath = Path.Combine(Path.GetTempPath(), "jsondrift-malformed-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    string malformedJson = malformed.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    File.WriteAllText(malformedPath, malformedJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    try
    {
        JsonDrift.Compare(SmokeContext.Default.RecursiveSmoke, malformedPath, JsonCompatibility.ReaderBackward);
        throw new InvalidOperationException("the packaged comparison accepted a reference-only baseline cycle");
    }
    catch (InvalidDataException)
    {
    }
}
finally
{
    File.Delete(malformedPath);
}

Console.WriteLine("consumer conservatism passed: R1-R6 public package regressions and positive controls");
}
finally
{
    File.Delete(baselinePath);
}

static JsonDriftReport Compare(JsonTypeInfo later, JsonTypeInfo earlier)
{
    string path = Path.Combine(Path.GetTempPath(), "jsondrift-pair-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        JsonBaseline.Create(earlier, path, overwrite: false);
        return JsonDrift.Compare(later, path, JsonCompatibility.ReaderBackward);
    }
    finally
    {
        File.Delete(path);
    }
}

static void RequireRejected(JsonDriftReport report, JsonDriftClassification? expected = null)
{
    if (expected is JsonDriftClassification classification && report.Outcome != classification ||
        expected is null && report.Outcome == JsonDriftClassification.Compatible)
    {
        throw new InvalidOperationException($"the packaged comparison returned {report.Outcome} for a rejected transition");
    }

    try
    {
        report.AssertCompatible();
        throw new InvalidOperationException("the packaged assertion accepted a rejected transition");
    }
    catch (JsonDriftCompatibilityException)
    {
    }
}

static void RequireCompatible(JsonDriftReport report)
{
    if (!report.IsCompatible)
    {
        throw new InvalidOperationException($"the packaged comparison rejected a positive control with {report.Outcome}");
    }

    report.AssertCompatible();
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

public sealed class OrderEnvelopeOptional
{
    public int Id { get; set; }

    public string? Note { get; set; }
}

public sealed class WritableSmoke
{
    public int Value { get; set; }
}

public sealed class GetterOnlySmoke
{
    public int Value { get; }
}

public enum SmokeState
{
    Ready = 1,
}

public sealed class IgnoredSmoke
{
    [JsonIgnore]
    public int Value { get; set; }
}

public sealed class IncludedRequiredSmoke
{
    [JsonRequired]
    public int Value { get; set; }
}

public sealed class RecursiveSmoke
{
    public int Value { get; set; }

    public RecursiveSmoke? Next { get; set; }
}

[JsonSerializable(typeof(OrderEnvelopeV1))]
[JsonSerializable(typeof(OrderEnvelopeV2))]
[JsonSerializable(typeof(OrderEnvelopeOptional))]
[JsonSerializable(typeof(WritableSmoke))]
[JsonSerializable(typeof(GetterOnlySmoke))]
[JsonSerializable(typeof(IgnoredSmoke))]
[JsonSerializable(typeof(IncludedRequiredSmoke))]
[JsonSerializable(typeof(RecursiveSmoke))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(HashSet<int>))]
public partial class SmokeContext : JsonSerializerContext
{
}
'@

function Get-ZipEntryBytes {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) {
        throw "candidate package is missing required entry: $Name"
    }

    $stream = $entry.Open()
    $memory = [IO.MemoryStream]::new()
    try {
        $stream.CopyTo($memory)
        return ,$memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $stream.Dispose()
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

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

    Add-Type -AssemblyName System.IO.Compression
    $candidateArchive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $candidateAssemblyHash = Get-Sha256 -Bytes (Get-ZipEntryBytes -Archive $candidateArchive -Name 'lib/net8.0/KeelMatrix.JsonDrift.dll')
    }
    finally {
        $candidateArchive.Dispose()
    }

    $restoredAssemblyPath = Join-Path $packages "keelmatrix.jsondrift/$packageVersion/lib/net8.0/KeelMatrix.JsonDrift.dll"
    if (-not (Test-Path -LiteralPath $restoredAssemblyPath -PathType Leaf)) {
        throw "restored package assembly was not found in the isolated cache: $restoredAssemblyPath"
    }
    $restoredAssemblyHash = Get-Sha256 -Bytes ([IO.File]::ReadAllBytes($restoredAssemblyPath))
    if ($candidateAssemblyHash -cne $restoredAssemblyHash) {
        throw "restored package assembly hash '$restoredAssemblyHash' does not match candidate '$candidateAssemblyHash'"
    }
    Write-Host "consumer package identity proof: candidate assembly sha256 $candidateAssemblyHash"

    & dotnet run --project (Join-Path $project 'Consumer.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "consumer run exited with code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
