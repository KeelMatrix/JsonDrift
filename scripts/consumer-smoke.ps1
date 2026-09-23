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
using System.Collections.ObjectModel;
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

RunExtensionDataInteractionRegressions();
RunPolymorphicMaterializationRegressions();
RunDictionaryMaterializationRegressions();
RunConcreteObjectMaterializationRegressions();
RunDiscriminatorMemberInteractionRegression();

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

Console.WriteLine("consumer conservatism passed: extension-data, concrete-object construction, hierarchy-wide discriminator-member interaction, polymorphism, and dictionary-materialization package regressions and positive controls");
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
        JsonContract written = JsonBaseline.Create(earlier, path, overwrite: false);
        JsonContract read = JsonBaseline.Read(path);
        if (written.CanonicalJson != read.CanonicalJson)
        {
            throw new InvalidOperationException("the packaged comparison baseline changed during write/read");
        }

        return JsonDrift.Compare(later, read, JsonCompatibility.ReaderBackward);
    }
    finally
    {
        File.Delete(path);
    }
}

static void RunExtensionDataInteractionRegressions()
{
    const string RenameDocument = "{\"account_id\":\"A\"}";
    string[] collisionTokens = { "\"not-a-number\"", "true", "{}", "[]", "null" };

    foreach (bool sourceGenerated in new[] { false, true })
    {
        JsonTypeInfo requiredEarlier = TypeInfo(typeof(RequiredRenameV1), sourceGenerated);
        JsonTypeInfo requiredLater = TypeInfo(typeof(RequiredRenameV2), sourceGenerated);
        RequireReadFailure<JsonException>(RenameDocument, requiredLater);
        JsonDriftReport requiredReport = Compare(requiredLater, requiredEarlier);
        RequireRejected(requiredReport, JsonDriftClassification.Incompatible);
        RequireRule(requiredReport, "R04.requiredness.optional-to-required");

        JsonTypeInfo safeEarlier = TypeInfo(typeof(OptionalRenameV1), sourceGenerated);
        JsonTypeInfo safeLater = TypeInfo(typeof(OptionalRenameV2WithExtensionData), sourceGenerated);
        var rebound = (OptionalRenameV2WithExtensionData?)JsonSerializer.Deserialize(RenameDocument, safeLater)
            ?? throw new InvalidOperationException("the packaged safe rename did not deserialize");
        if (rebound.Extra["account_id"].GetString() != "A")
        {
            throw new InvalidOperationException("the packaged safe rename did not preserve the earlier key");
        }
        JsonDriftReport safeRename = Compare(safeLater, safeEarlier);
        RequireCompatible(safeRename);
        RequireRule(safeRename, "R03.serialized-name.mitigated-by-extension-data");

        JsonTypeInfo extensionEarlier = TypeInfo(typeof(ExtensionDataOnly), sourceGenerated);
        JsonTypeInfo extensionLater = TypeInfo(typeof(ExtensionDataWithCount), sourceGenerated);
        foreach (string token in collisionTokens)
        {
            using JsonDocument value = JsonDocument.Parse(token);
            var instance = new ExtensionDataOnly
            {
                Extra = new Dictionary<string, JsonElement>
                {
                    ["Count"] = value.RootElement.Clone(),
                },
            };
            string earlierDocument = JsonSerializer.Serialize(instance, extensionEarlier);
            RequireReadFailure<JsonException>(earlierDocument, extensionLater);
        }
        JsonDriftReport collision = Compare(extensionLater, extensionEarlier);
        RequireRejected(collision, JsonDriftClassification.Unsupported);
        RequireRule(collision, "R01.property-add.extension-data-key-collision");

        JsonTypeInfo enumLater = TypeInfo(typeof(ExtensionDataWithState), sourceGenerated);
        const string EnumDocument = "{\"State\":\"not-a-state\"}";
        RequireReadFailure<JsonException>(EnumDocument, enumLater);
        JsonDriftReport enumCollision = Compare(enumLater, extensionEarlier);
        RequireRejected(enumCollision, JsonDriftClassification.Unsupported);
        RequireRule(enumCollision, "R01.property-add.extension-data-key-collision");

        JsonTypeInfo polymorphismEarlier = TypeInfo(typeof(ExtensionDataBeforePolymorphism), sourceGenerated);
        JsonTypeInfo polymorphismLater = TypeInfo(typeof(ExtensionDataWithPolymorphism), sourceGenerated);
        using JsonDocument unknownDiscriminator = JsonDocument.Parse("\"unknown\"");
        var polymorphismInstance = new ExtensionDataBeforePolymorphism
        {
            Extra = new Dictionary<string, JsonElement>
            {
                ["$type"] = unknownDiscriminator.RootElement.Clone(),
            },
        };
        string polymorphismDocument = JsonSerializer.Serialize(polymorphismInstance, polymorphismEarlier);
        if (polymorphismDocument != "{\"$type\":\"unknown\"}")
        {
            throw new InvalidOperationException($"the packaged extension-data witness changed: {polymorphismDocument}");
        }

        try
        {
            JsonSerializer.Deserialize(polymorphismDocument, polymorphismLater);
            throw new InvalidOperationException("the packaged polymorphic reader accepted an unknown discriminator");
        }
        catch (JsonException exception)
        {
            if (!exception.Message.Contains("Read unrecognized type discriminator id 'unknown'.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("the packaged polymorphic reader failed for an unexpected reason", exception);
            }
        }

        JsonDriftReport polymorphismCollision = Compare(polymorphismLater, polymorphismEarlier);
        RequireRejected(polymorphismCollision, JsonDriftClassification.Unsupported);
        RequireRule(polymorphismCollision, "R10c.polymorphism.extension-data-discriminator-collision");

        RequireCompatible(Compare(
            TypeInfo(typeof(OrdinaryAdditionV2), sourceGenerated),
            TypeInfo(typeof(OrdinaryAdditionV1), sourceGenerated)));
    }
}

static void RunDictionaryMaterializationRegressions()
{
    foreach (bool sourceGenerated in new[] { false, true })
    {
        JsonTypeInfo dictionary = TypeInfo(typeof(Dictionary<string, int>), sourceGenerated);
        JsonTypeInfo readOnlyDictionary = TypeInfo(typeof(ReadOnlyDictionary<string, int>), sourceGenerated);
        string rootDocument = JsonSerializer.Serialize(new Dictionary<string, int> { ["a"] = 1 }, dictionary);
        RequireReadFailure<NotSupportedException>(rootDocument, readOnlyDictionary);
        JsonDriftReport root = Compare(readOnlyDictionary, dictionary);
        RequireRejected(root, JsonDriftClassification.Unsupported);
        RequireRule(root, "unsupported.dictionary-materialization-unproven");

        JsonTypeInfo writableHolder = TypeInfo(typeof(WritableDictionaryHolder), sourceGenerated);
        JsonTypeInfo readOnlyHolder = TypeInfo(typeof(ReadOnlyDictionaryHolder), sourceGenerated);
        string nestedDocument = JsonSerializer.Serialize(
            new WritableDictionaryHolder { Values = { ["a"] = 1 } },
            writableHolder);
        RequireReadFailure<NotSupportedException>(nestedDocument, readOnlyHolder);
        JsonDriftReport nested = Compare(readOnlyHolder, writableHolder);
        RequireRejected(nested, JsonDriftClassification.Unsupported);
        RequireRule(nested, "unsupported.dictionary-materialization-unproven");

        RequireCompatible(Compare(dictionary, dictionary));
        RequireCompatible(Compare(writableHolder, writableHolder));
    }
}

static void RunConcreteObjectMaterializationRegressions()
{
    const string EarlierDocument = "{\"Value\":7}";

    foreach (bool sourceGenerated in new[] { false, true })
    {
        JsonTypeInfo earlier = TypeInfo(typeof(PublicParameterlessValue), sourceGenerated);
        JsonTypeInfo privateOnly = TypeInfo(typeof(PrivateConstructorOnlyValue), sourceGenerated);
        string document = JsonSerializer.Serialize(new PublicParameterlessValue { Value = 7 }, earlier);
        if (document != EarlierDocument)
        {
            throw new InvalidOperationException($"the packaged concrete-construction witness changed: {document}");
        }

        RequireReadFailure<NotSupportedException>(document, privateOnly);
        JsonDriftReport root = Compare(privateOnly, earlier);
        RequireRejected(root, JsonDriftClassification.Unsupported);
        RequireRule(root, "unsupported.object-materialization-unproven");

        JsonTypeInfo earlierHolder = TypeInfo(typeof(PublicConstructibleHolder), sourceGenerated);
        JsonTypeInfo privateHolder = TypeInfo(typeof(PrivateConstructibleHolder), sourceGenerated);
        string nestedDocument = JsonSerializer.Serialize(
            new PublicConstructibleHolder { Value = new PublicParameterlessValue { Value = 7 } },
            earlierHolder);
        RequireReadFailure<NotSupportedException>(nestedDocument, privateHolder);
        JsonDriftReport nested = Compare(privateHolder, earlierHolder);
        RequireRejected(nested, JsonDriftClassification.Unsupported);
        RequireRule(nested, "unsupported.object-materialization-unproven");

        JsonTypeInfo ambiguous = TypeInfo(typeof(AmbiguousConstructorValue), sourceGenerated);
        RequireReadFailure<NotSupportedException>(document, ambiguous);
        JsonDriftReport ambiguousReport = Compare(ambiguous, earlier);
        RequireRejected(ambiguousReport, JsonDriftClassification.Unsupported);
        RequireRule(ambiguousReport, "unsupported.object-materialization-unproven");

        RequireObjectMaterialization(
            new PublicParameterlessValue { Value = 7 },
            TypeInfo(typeof(PublicParameterlessValue), sourceGenerated));
        RequireObjectMaterialization(
            new SingleParameterizedValue(7),
            TypeInfo(typeof(SingleParameterizedValue), sourceGenerated));
        RequireObjectMaterialization(
            new JsonConstructorValue(7),
            TypeInfo(typeof(JsonConstructorValue), sourceGenerated));
    }
}

static void RunDiscriminatorMemberInteractionRegression()
{
    const string EarlierDocument = "{\"kind\":\"ordinary\"}";

    foreach (bool sourceGenerated in new[] { false, true })
    {
        JsonTypeInfo earlier = TypeInfo(typeof(OrdinaryKindMember), sourceGenerated);
        JsonTypeInfo later = sourceGenerated
            ? ((IJsonTypeInfoResolver)SmokeContext.Default).GetTypeInfo(
                typeof(CollidingPolymorphicKind),
                SmokeContext.Default.Options) ?? throw new InvalidOperationException("source-generated collision metadata is missing")
            : TypeInfo(typeof(CollidingPolymorphicKind), sourceGenerated: false);
        string document = JsonSerializer.Serialize(new OrdinaryKindMember { kind = "ordinary" }, earlier);
        if (document != EarlierDocument)
        {
            throw new InvalidOperationException($"the packaged discriminator/member witness changed: {document}");
        }

        bool preserved = false;
        try
        {
            object? rebound = JsonSerializer.Deserialize(document, later);
            string reboundDocument = JsonSerializer.Serialize(rebound, later);
            preserved = JsonNode.DeepEquals(JsonNode.Parse(document), JsonNode.Parse(reboundDocument));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
        {
        }

        if (preserved)
        {
            throw new InvalidOperationException("the packaged colliding discriminator/member reader unexpectedly preserved the ordinary member");
        }

        JsonDriftReport report = Compare(later, earlier);
        RequireRejected(report, JsonDriftClassification.Unsupported);
        RequireRule(report, "unsupported.polymorphism-discriminator-member-collision");

        RequireDiscriminatorCollision(
            "derived-member",
            new PlainAnimal { Name = "Fido" },
            typeof(PlainAnimal),
            typeof(DerivedMemberCollisionBase),
            "{\"Name\":\"Fido\"}",
            sourceGenerated);
        RequireDiscriminatorCollision(
            "derived-of-derived",
            new PlainKindAnimal { Name = "Fido", kind = "ordinary" },
            typeof(PlainKindAnimal),
            typeof(MultiLevelCollisionBase),
            "{\"Name\":\"Fido\",\"kind\":\"ordinary\"}",
            sourceGenerated);
        RequireDiscriminatorCollision(
            "renamed-derived-member",
            new PlainAnimal { Name = "Fido" },
            typeof(PlainAnimal),
            typeof(RenamedDerivedMemberCollisionBase),
            "{\"Name\":\"Fido\"}",
            sourceGenerated);

        JsonTypeInfo nonColliding = TypeInfo(typeof(ConcretePolymorphicAnimal), sourceGenerated);
        JsonTypeInfo nonPolymorphic = TypeInfo(typeof(ConcreteAnimal), sourceGenerated);
        JsonDriftReport positive = Compare(nonColliding, nonPolymorphic);
        RequireCompatible(positive);
        RequireRule(positive, "R10e.polymorphism.dispatch-added-concrete");
    }
}

static void RequireDiscriminatorCollision(
    string caseName,
    object earlierValue,
    Type earlierType,
    Type laterType,
    string expectedDocument,
    bool sourceGenerated)
{
    JsonTypeInfo earlier = TypeInfo(earlierType, sourceGenerated);
    JsonTypeInfo later = sourceGenerated
        ? ((IJsonTypeInfoResolver)SmokeContext.Default).GetTypeInfo(
            laterType,
            SmokeContext.Default.Options) ?? throw new InvalidOperationException($"source-generated metadata is missing for {laterType}")
        : TypeInfo(laterType, sourceGenerated: false);
    string document = JsonSerializer.Serialize(earlierValue, earlier);
    if (document != expectedDocument)
    {
        throw new InvalidOperationException($"the packaged {caseName} witness changed: {document}");
    }

    bool rejected = false;
    try
    {
        JsonSerializer.Deserialize(document, later);
    }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException)
    {
        rejected = true;
    }

    if (!rejected)
    {
        throw new InvalidOperationException($"the packaged {caseName} reader unexpectedly accepted {document}");
    }

    JsonDriftReport report = Compare(later, earlier);
    RequireRejected(report, JsonDriftClassification.Unsupported);
    RequireRule(report, "unsupported.polymorphism-discriminator-member-collision");

    string metadataMode = sourceGenerated ? "source-generated" : "reflection";
    Console.WriteLine($"discriminator/member collision passed: case={caseName}; mode={metadataMode}; json={document}; baselineRoundTrip=True; verdict={report.Outcome}; assertion=threw");
}

static void RequireObjectMaterialization(object value, JsonTypeInfo contract)
{
    string document = JsonSerializer.Serialize(value, contract);
    object? rebound = JsonSerializer.Deserialize(document, contract);
    if (rebound is null ||
        !JsonNode.DeepEquals(JsonNode.Parse(document), JsonNode.Parse(JsonSerializer.Serialize(rebound, contract))))
    {
        throw new InvalidOperationException($"the packaged object materialization positive control lost data: {contract.Type}");
    }

    RequireCompatible(Compare(contract, contract));
}

static void RunPolymorphicMaterializationRegressions()
{
    const string EarlierDocument = "{\"Name\":\"A\"}";

    foreach (bool sourceGenerated in new[] { false, true })
    {
        JsonTypeInfo earlier = TypeInfo(typeof(ConcreteAnimal), sourceGenerated);
        JsonTypeInfo later = TypeInfo(typeof(AbstractAnimal), sourceGenerated);
        string earlierDocument = JsonSerializer.Serialize(new ConcreteAnimal { Name = "A" }, earlier);
        if (earlierDocument != EarlierDocument)
        {
            throw new InvalidOperationException($"the packaged concrete-animal witness changed: {earlierDocument}");
        }

        try
        {
            JsonSerializer.Deserialize(earlierDocument, later);
            throw new InvalidOperationException("the packaged abstract polymorphic reader accepted a missing discriminator");
        }
        catch (NotSupportedException exception)
        {
            string expected = $"The JSON payload for polymorphic interface or abstract type '{typeof(AbstractAnimal)}' must specify a type discriminator.";
            if (!exception.Message.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("the packaged abstract polymorphic reader failed for an unexpected reason", exception);
            }
        }

        string path = Path.Combine(Path.GetTempPath(), "jsondrift-polymorphic-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonContract written = JsonBaseline.Create(earlier, path, overwrite: false);
            JsonContract read = JsonBaseline.Read(path);
            if (written.CanonicalJson != read.CanonicalJson)
            {
                throw new InvalidOperationException("the packaged polymorphic baseline changed during write/read");
            }

            JsonDriftReport report = JsonDrift.Compare(later, read, JsonCompatibility.ReaderBackward);
            RequireRejected(report, JsonDriftClassification.Incompatible);
            RequireRule(report, "R10d.polymorphism.discriminator-required");
        }
        finally
        {
            File.Delete(path);
        }

        JsonTypeInfo concreteLater = TypeInfo(typeof(ConcretePolymorphicAnimal), sourceGenerated);
        if (JsonSerializer.Deserialize(earlierDocument, concreteLater) is not ConcretePolymorphicAnimal)
        {
            throw new InvalidOperationException("the packaged concrete polymorphic positive control did not materialize");
        }

        JsonDriftReport concreteReport = Compare(concreteLater, earlier);
        RequireCompatible(concreteReport);
        RequireRule(concreteReport, "R10e.polymorphism.dispatch-added-concrete");

        RequireCompatible(Compare(earlier, earlier));
        string derivedDocument = JsonSerializer.Serialize(new AbstractDog { Name = "A" }, later);
        if (JsonSerializer.Deserialize(derivedDocument, later) is not AbstractDog)
        {
            throw new InvalidOperationException("the packaged polymorphic derived-type positive control did not materialize");
        }
        RequireCompatible(Compare(later, later));

        foreach (Type plainType in new[] { typeof(PlainAbstractAnimal), typeof(IPlainAnimal) })
        {
            JsonTypeInfo plainLater = TypeInfo(plainType, sourceGenerated);
            RequireReadFailure<NotSupportedException>(earlierDocument, plainLater);

            string plainPath = Path.Combine(Path.GetTempPath(), "jsondrift-plain-reader-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                JsonContract written = JsonBaseline.Create(earlier, plainPath, overwrite: false);
                JsonContract read = JsonBaseline.Read(plainPath);
                if (written.CanonicalJson != read.CanonicalJson)
                {
                    throw new InvalidOperationException("the packaged plain-reader baseline changed during write/read");
                }

                JsonDriftReport plainReport = JsonDrift.Compare(plainLater, read, JsonCompatibility.ReaderBackward);
                RequireRejected(plainReport, JsonDriftClassification.Unsupported);
                RequireRule(plainReport, "unsupported.object-materialization-unproven");

                string metadataMode = sourceGenerated ? "source-generated" : "reflection";
                string contractKind = plainType.IsInterface ? "interface" : "abstract";
                Console.WriteLine($"plain reader materialization passed: mode={metadataMode}; contract={contractKind}; json={earlierDocument}; baselineRoundTrip=True; verdict={plainReport.Outcome}; assertion=threw");
            }
            finally
            {
                File.Delete(plainPath);
            }
        }
    }
}

static JsonTypeInfo TypeInfo(Type type, bool sourceGenerated)
{
    if (sourceGenerated)
    {
        return SmokeContext.Default.GetTypeInfo(type)
            ?? throw new InvalidOperationException($"source-generated metadata is missing for {type}");
    }

    var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    return options.GetTypeInfo(type);
}

static void RequireReadFailure<TException>(string document, JsonTypeInfo later)
    where TException : Exception
{
    try
    {
        JsonSerializer.Deserialize(document, later);
        throw new InvalidOperationException($"the later reader unexpectedly accepted {document}");
    }
    catch (TException)
    {
    }
}

static void RequireRule(JsonDriftReport report, string ruleId)
{
    if (!report.Changes.Any(change => change.RuleId == ruleId))
    {
        throw new InvalidOperationException($"the packaged comparison did not report {ruleId}");
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

public sealed class RequiredRenameV1
{
    [JsonRequired]
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class RequiredRenameV2
{
    [JsonRequired]
    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class OptionalRenameV1
{
    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }
}

public sealed class OptionalRenameV2WithExtensionData
{
    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class ExtensionDataOnly
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class ExtensionDataWithCount
{
    public int Count { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class ExtensionDataWithState
{
    public CollisionState State { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class ExtensionDataBeforePolymorphism
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ExtensionDataDog), "dog")]
public class ExtensionDataWithPolymorphism
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();
}

public sealed class ExtensionDataDog : ExtensionDataWithPolymorphism
{
    public int BarkVolume { get; set; }
}

public enum CollisionState
{
    Ready = 1,
}

public sealed class OrdinaryAdditionV1
{
    public int Id { get; set; }
}

public sealed class OrdinaryAdditionV2
{
    public int Id { get; set; }

    public int Count { get; set; }
}

public sealed class ConcreteAnimal
{
    public string Name { get; set; } = string.Empty;
}

public abstract class PlainAbstractAnimal
{
    public string Name { get; set; } = string.Empty;
}

public interface IPlainAnimal
{
    string Name { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AbstractDog), "dog")]
public abstract class AbstractAnimal
{
    public string Name { get; set; } = string.Empty;
}

public sealed class AbstractDog : AbstractAnimal
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ConcreteDog), "dog")]
public class ConcretePolymorphicAnimal
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ConcreteDog : ConcretePolymorphicAnimal
{
}

public sealed class PublicParameterlessValue
{
    public int Value { get; set; }
}

public sealed class PrivateConstructorOnlyValue
{
    private PrivateConstructorOnlyValue()
    {
    }

    public int Value { get; set; }
}

public sealed class PublicConstructibleHolder
{
    public PublicParameterlessValue Value { get; set; } = new();
}

public sealed class PrivateConstructibleHolder
{
    public PrivateConstructorOnlyValue Value { get; set; } = null!;
}

public sealed class AmbiguousConstructorValue
{
    public AmbiguousConstructorValue(int value) => Value = value;

    public AmbiguousConstructorValue(string value) => Value = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    public int Value { get; }
}

public sealed class SingleParameterizedValue
{
    public SingleParameterizedValue(int value) => Value = value;

    public int Value { get; }
}

public sealed class JsonConstructorValue
{
    public JsonConstructorValue() => Value = -1;

    [JsonConstructor]
    public JsonConstructorValue(int value) => Value = value;

    public int Value { get; }
}

public sealed class OrdinaryKindMember
{
    public string kind { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CollidingPolymorphicKindDog), "dog")]
public class CollidingPolymorphicKind
{
    public string kind { get; set; } = string.Empty;
}

public sealed class CollidingPolymorphicKindDog : CollidingPolymorphicKind
{
}

public sealed class PlainAnimal
{
    public string Name { get; set; } = string.Empty;
}

public sealed class PlainKindAnimal
{
    public string Name { get; set; } = string.Empty;

    public string kind { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DerivedMemberCollisionDog), "dog")]
public class DerivedMemberCollisionBase
{
    public string Name { get; set; } = string.Empty;
}

public sealed class DerivedMemberCollisionDog : DerivedMemberCollisionBase
{
    public string kind { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MultiLevelCollisionBranch), "branch")]
public class MultiLevelCollisionBase
{
    public string Name { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "branchKind")]
[JsonDerivedType(typeof(MultiLevelCollisionLeaf), "leaf")]
public class MultiLevelCollisionBranch : MultiLevelCollisionBase
{
}

public sealed class MultiLevelCollisionLeaf : MultiLevelCollisionBranch
{
    public string kind { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RenamedDerivedMemberCollisionDog), "dog")]
public class RenamedDerivedMemberCollisionBase
{
    public string Name { get; set; } = string.Empty;
}

public sealed class RenamedDerivedMemberCollisionDog : RenamedDerivedMemberCollisionBase
{
    [JsonPropertyName("kind")]
    public string LegacyKind { get; set; } = string.Empty;
}

public sealed class WritableDictionaryHolder
{
    public Dictionary<string, int> Values { get; set; } = new();
}

public sealed class ReadOnlyDictionaryHolder
{
    public ReadOnlyDictionary<string, int> Values { get; set; } = new(new Dictionary<string, int>());
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
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(ReadOnlyDictionary<string, int>))]
[JsonSerializable(typeof(RequiredRenameV1))]
[JsonSerializable(typeof(RequiredRenameV2))]
[JsonSerializable(typeof(OptionalRenameV1))]
[JsonSerializable(typeof(OptionalRenameV2WithExtensionData))]
[JsonSerializable(typeof(ExtensionDataOnly))]
[JsonSerializable(typeof(ExtensionDataWithCount))]
[JsonSerializable(typeof(ExtensionDataWithState))]
[JsonSerializable(typeof(ExtensionDataBeforePolymorphism))]
[JsonSerializable(typeof(ExtensionDataWithPolymorphism))]
[JsonSerializable(typeof(ExtensionDataDog))]
[JsonSerializable(typeof(OrdinaryAdditionV1))]
[JsonSerializable(typeof(OrdinaryAdditionV2))]
[JsonSerializable(typeof(ConcreteAnimal))]
[JsonSerializable(typeof(PlainAbstractAnimal))]
[JsonSerializable(typeof(IPlainAnimal))]
[JsonSerializable(typeof(AbstractAnimal))]
[JsonSerializable(typeof(AbstractDog))]
[JsonSerializable(typeof(ConcretePolymorphicAnimal))]
[JsonSerializable(typeof(ConcreteDog))]
[JsonSerializable(typeof(PublicParameterlessValue))]
[JsonSerializable(typeof(PrivateConstructorOnlyValue))]
[JsonSerializable(typeof(PublicConstructibleHolder))]
[JsonSerializable(typeof(PrivateConstructibleHolder))]
[JsonSerializable(typeof(AmbiguousConstructorValue))]
[JsonSerializable(typeof(SingleParameterizedValue))]
[JsonSerializable(typeof(JsonConstructorValue))]
[JsonSerializable(typeof(OrdinaryKindMember))]
[JsonSerializable(typeof(CollidingPolymorphicKind))]
[JsonSerializable(typeof(CollidingPolymorphicKindDog))]
[JsonSerializable(typeof(PlainAnimal))]
[JsonSerializable(typeof(PlainKindAnimal))]
[JsonSerializable(typeof(DerivedMemberCollisionBase))]
[JsonSerializable(typeof(DerivedMemberCollisionDog))]
[JsonSerializable(typeof(MultiLevelCollisionBase))]
[JsonSerializable(typeof(MultiLevelCollisionBranch))]
[JsonSerializable(typeof(MultiLevelCollisionLeaf))]
[JsonSerializable(typeof(RenamedDerivedMemberCollisionBase))]
[JsonSerializable(typeof(RenamedDerivedMemberCollisionDog))]
[JsonSerializable(typeof(WritableDictionaryHolder))]
[JsonSerializable(typeof(ReadOnlyDictionaryHolder))]
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
