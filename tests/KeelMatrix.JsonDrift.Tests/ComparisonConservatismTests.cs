using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.Internal;

namespace KeelMatrix.JsonDrift.Tests;

public sealed class ComparisonConservatismTests
{
    [Fact]
    public void EveryRecordedFactHasAComparisonRuleOrNonContractReason()
    {
        Assert.Empty(ComparisonFactRules.ValidateCoverage());

        string[] methods = typeof(ComparisonConservatismTests)
            .GetMethods()
            .Select(static method => method.Name)
            .ToArray();
        string[] missingTestWitnesses = ComparisonFactRules.Entries
            .Where(static entry => entry.Witness is not null)
            .SelectMany(static entry => entry.Witness!.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .Where(static witness => witness.StartsWith("test:", StringComparison.Ordinal))
            .Select(static witness => witness["test:".Length..])
            .Where(witness => !methods.Contains(witness, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingTestWitnesses);
    }

    [Fact]
    public void NullableValueAcceptanceIsRetainedAtEveryValueSlot()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo nullableRoot = TypeInfo(typeof(int?), sourceGenerated);
            JsonTypeInfo nonNullableRoot = TypeInfo(typeof(int), sourceGenerated);
            Assert.False(WireReadIsLossless(null, nullableRoot, nonNullableRoot));
            AssertRejected(Compare(nonNullableRoot, nullableRoot));
            AssertCompatible(Compare(nullableRoot, nonNullableRoot));

            JsonTypeInfo nullableList = TypeInfo(typeof(List<int?>), sourceGenerated);
            JsonTypeInfo nonNullableList = TypeInfo(typeof(List<int>), sourceGenerated);
            Assert.False(WireReadIsLossless(new List<int?> { null }, nullableList, nonNullableList));
            AssertRejected(Compare(nonNullableList, nullableList));
            AssertCompatible(Compare(nullableList, nonNullableList));

            JsonTypeInfo nullableDictionary = TypeInfo(typeof(Dictionary<string, int?>), sourceGenerated);
            JsonTypeInfo nonNullableDictionary = TypeInfo(typeof(Dictionary<string, int>), sourceGenerated);
            Assert.False(WireReadIsLossless(
                new Dictionary<string, int?> { ["a"] = null },
                nullableDictionary,
                nonNullableDictionary));
            AssertRejected(Compare(nonNullableDictionary, nullableDictionary));
            AssertCompatible(Compare(nullableDictionary, nonNullableDictionary));
        }
    }

    [Fact]
    public void RemovingMemberMaterializationCapabilityDoesNotReportCompatible()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo writable = TypeInfo(typeof(WritableValue), sourceGenerated);
            JsonTypeInfo getterOnly = TypeInfo(typeof(GetterOnlyValue), sourceGenerated);
            JsonTypeInfo constructorBound = TypeInfo(typeof(ConstructorBoundValue), sourceGenerated);

            Assert.False(WireReadIsLossless(new WritableValue { Value = 7 }, writable, getterOnly));
            AssertRejected(Compare(getterOnly, writable));

            Assert.True(WireReadIsLossless(new GetterOnlyValue(), getterOnly, writable));
            AssertRejected(Compare(writable, getterOnly), JsonDriftClassification.Unsupported);

            Assert.True(WireReadIsLossless(new WritableValue { Value = 7 }, writable, constructorBound));
            JsonDriftReport constructorReport = Compare(constructorBound, writable);
            AssertCompatible(constructorReport);
            Assert.Contains(constructorReport.Changes, change =>
                change.RuleId == "R12.binding.constructor-materializer-added");

            Assert.True(WireReadIsLossless(new ConstructorBoundValue(7), constructorBound, writable));
            JsonDriftReport unwitnessedReverse = Compare(writable, constructorBound);
            AssertRejected(unwitnessedReverse, JsonDriftClassification.Unsupported);
            Assert.Contains(unwitnessedReverse.Changes, change =>
                change.RuleId == "unsupported.comparison-rule-gap");
        }
    }

    [Fact]
    public void CollectionTransitionsRequireOrderingAndMultiplicityPreservation()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo list = TypeInfo(typeof(List<int>), sourceGenerated);
            JsonTypeInfo array = TypeInfo(typeof(int[]), sourceGenerated);
            JsonTypeInfo set = TypeInfo(typeof(HashSet<int>), sourceGenerated);
            JsonTypeInfo sortedSet = TypeInfo(typeof(SortedSet<int>), sourceGenerated);

            var duplicateAndOrdered = new List<int> { 2, 1, 1 };
            Assert.False(WireReadIsLossless(duplicateAndOrdered, list, set));
            AssertRejected(Compare(set, list), JsonDriftClassification.Unsupported);

            Assert.False(WireReadIsLossless(new List<int> { 2, 1 }, list, sortedSet));
            AssertRejected(Compare(sortedSet, list), JsonDriftClassification.Unsupported);

            Assert.True(WireReadIsLossless(duplicateAndOrdered, list, array));
            JsonDriftReport listToArray = Compare(array, list);
            AssertCompatible(listToArray);
            Assert.Contains(listToArray.Changes, change => change.RuleId == "R07.shape.collection-preservation");
            Assert.True(WireReadIsLossless(new List<int> { 2, 1, 1 }.ToArray(), array, list));
            JsonDriftReport arrayToList = Compare(list, array);
            AssertCompatible(arrayToList);
            Assert.Contains(arrayToList.Changes, change => change.RuleId == "R07.shape.collection-preservation");
        }
    }

    [Fact]
    public void EnumIntegerTokenAcceptanceIsComparedAtRootAndNestedSlots()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonSerializerOptions acceptsIntegers = EnumOptions(sourceGenerated, allowIntegerValues: true);
            JsonSerializerOptions rejectsIntegers = EnumOptions(sourceGenerated, allowIntegerValues: false);
            JsonTypeInfo earlierRoot = acceptsIntegers.GetTypeInfo(typeof(WireState));
            JsonTypeInfo laterRoot = rejectsIntegers.GetTypeInfo(typeof(WireState));

            Assert.False(WireReadIsLossless((WireState)42, earlierRoot, laterRoot));
            AssertRejected(Compare(laterRoot, earlierRoot));
            AssertCompatible(Compare(earlierRoot, laterRoot));

            JsonTypeInfo earlierNested = acceptsIntegers.GetTypeInfo(typeof(EnumEnvelope));
            JsonTypeInfo laterNested = rejectsIntegers.GetTypeInfo(typeof(EnumEnvelope));
            Assert.False(WireReadIsLossless(
                new EnumEnvelope { State = (WireState)42 },
                earlierNested,
                laterNested));
            AssertRejected(Compare(laterNested, earlierNested));
            AssertCompatible(Compare(earlierNested, laterNested));
        }
    }

    [Fact]
    public void IndependentMemberConstraintsAreReportedTogether()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo ignored = TypeInfo(typeof(IgnoredValue), sourceGenerated);
            JsonTypeInfo includedRequired = TypeInfo(typeof(IncludedRequiredValue), sourceGenerated);
            Assert.False(WireReadIsLossless(new IgnoredValue(), ignored, includedRequired));

            JsonDriftReport inclusionReport = Compare(includedRequired, ignored);
            Assert.Equal(JsonDriftClassification.Incompatible, inclusionReport.Outcome);
            Assert.Contains(inclusionReport.Changes, change => change.RuleId == "R09.ignore.member-included");
            Assert.Contains(inclusionReport.Changes, change => change.RuleId == "R04.requiredness.optional-to-required");
            Assert.Throws<JsonDriftCompatibilityException>(() => inclusionReport.AssertCompatible());

            JsonTypeInfo beforeConstructorAddition = TypeInfo(typeof(ConstructorBeforeAddition), sourceGenerated);
            JsonTypeInfo afterConstructorAddition = TypeInfo(typeof(ConstructorRequiredAddition), sourceGenerated);
            Assert.False(WireReadIsLossless(
                new ConstructorBeforeAddition(1),
                beforeConstructorAddition,
                afterConstructorAddition));

            JsonDriftReport constructorReport = Compare(afterConstructorAddition, beforeConstructorAddition);
            Assert.Equal(JsonDriftClassification.Incompatible, constructorReport.Outcome);
            Assert.Contains(constructorReport.Changes, change => change.RuleId == "R12.binding.constructor-parameter-added");
            Assert.Contains(constructorReport.Changes, change => change.RuleId == "R04.requiredness.optional-to-required");
            Assert.Throws<JsonDriftCompatibilityException>(() => constructorReport.AssertCompatible());

            JsonTypeInfo ordinaryAddition = TypeInfo(typeof(OrdinaryAddition), sourceGenerated);
            AssertCompatible(Compare(ordinaryAddition, beforeConstructorAddition));
        }
    }

    [Fact]
    public void ReferenceOnlyDanglingAndAmbiguousGraphsAreRejectedBeforeComparison()
    {
        JsonTypeInfo current = TypeInfo(typeof(RecursiveValue), sourceGenerated: false);
        JsonContract valid = JsonDrift.Extract(current);

        string selfReference = ReplaceRootWithReference(valid.CanonicalJson, "root");
        AssertMalformedBaselineRejected(selfReference, current);

        string danglingReference = ReplaceRootWithReference(valid.CanonicalJson, "root.missing");
        AssertMalformedBaselineRejected(danglingReference, current);

        JsonObject ambiguous = JsonNode.Parse(valid.CanonicalJson)!.AsObject();
        JsonObject root = ambiguous["root"]!.AsObject();
        JsonObject memberShape = root["members"]!.AsArray()[0]!["shape"]!.AsObject();
        memberShape["path"] = "root";
        AssertMalformedBaselineRejected(ToCanonicalJson(ambiguous), current);

        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo recursive = TypeInfo(typeof(RecursiveValue), sourceGenerated);
            AssertCompatible(Compare(recursive, recursive));
        }
    }

    [Fact]
    public void SoundNumericWideningRemainsCompatible()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo integer = TypeInfo(typeof(int), sourceGenerated);
            JsonTypeInfo longer = TypeInfo(typeof(long), sourceGenerated);
            Assert.True(WireReadIsLossless(int.MaxValue, integer, longer));
            AssertCompatible(Compare(longer, integer));
        }
    }

    private static IEnumerable<bool> MetadataPaths()
    {
        yield return false;
        yield return true;
    }

    private static JsonTypeInfo TypeInfo(Type type, bool sourceGenerated) =>
        sourceGenerated
            ? ComparisonSourceContext.Default.GetTypeInfo(type) ?? throw new InvalidOperationException($"Missing generated metadata for {type}.")
            : ReflectionOptions().GetTypeInfo(type);

    private static JsonSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonSerializerOptions EnumOptions(bool sourceGenerated, bool allowIntegerValues)
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = sourceGenerated ? ComparisonSourceContext.Default : new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues));
        return options;
    }

    private static JsonDriftReport Compare(JsonTypeInfo later, JsonTypeInfo earlier) =>
        JsonDrift.Compare(later, RoundTripBaseline(JsonDrift.Extract(earlier)), JsonCompatibility.ReaderBackward);

    private static void AssertRejected(
        JsonDriftReport report,
        JsonDriftClassification? expected = null)
    {
        if (expected is JsonDriftClassification classification)
        {
            Assert.Equal(classification, report.Outcome);
        }
        else
        {
            Assert.NotEqual(JsonDriftClassification.Compatible, report.Outcome);
        }

        Assert.NotEmpty(report.Changes);
        Assert.Throws<JsonDriftCompatibilityException>(() => report.AssertCompatible());
    }

    private static void AssertCompatible(JsonDriftReport report)
    {
        Assert.True(
            report.Outcome == JsonDriftClassification.Compatible,
            string.Join(Environment.NewLine, report.Changes.Select(static change =>
                $"{change.Classification} {change.RuleId} {change.Path}: {change.Reason}")));
        report.AssertCompatible();
    }

    private static bool WireReadIsLossless(object? value, JsonTypeInfo earlier, JsonTypeInfo later)
    {
        string document = JsonSerializer.Serialize(value, earlier);

        try
        {
            object? rebound = JsonSerializer.Deserialize(document, later);
            string reboundDocument = JsonSerializer.Serialize(rebound, later);
            return JsonNode.DeepEquals(JsonNode.Parse(document), JsonNode.Parse(reboundDocument));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonContract RoundTripBaseline(JsonContract contract) => ReadCanonical(contract.CanonicalJson);

    private static void AssertMalformedBaselineRejected(string canonicalJson, JsonTypeInfo current)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(canonicalJson);
        Assert.Throws<InvalidDataException>(() =>
            JsonContract.FromCanonicalJson(bytes, JsonBaselineLimits.Default));

        string directory = Path.Combine(Path.GetTempPath(), "jsondrift-comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "baseline.json");

        try
        {
            File.WriteAllText(path, canonicalJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(path));
            Assert.Throws<InvalidDataException>(() =>
                JsonDrift.Compare(current, path, JsonCompatibility.ReaderBackward));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonContract ReadCanonical(string canonicalJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "jsondrift-comparison-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "baseline.json");

        try
        {
            File.WriteAllText(path, canonicalJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return JsonBaseline.Read(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReplaceRootWithReference(string canonicalJson, string reference)
    {
        JsonObject document = JsonNode.Parse(canonicalJson)!.AsObject();
        document["root"] = new JsonObject
        {
            ["typeName"] = typeof(RecursiveValue).FullName,
            ["kind"] = "reference",
            ["acceptsNull"] = false,
            ["reachedBy"] = "resolver-chain",
            ["path"] = "root",
            ["rule"] = "supported.reference",
            ["supported"] = true,
            ["declaredAttributes"] = new JsonArray(),
            ["reference"] = reference,
        };
        document["overallSupported"] = true;
        return ToCanonicalJson(document);
    }

    private static string ToCanonicalJson(JsonObject document) =>
        string.Concat(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal), "\n");

    internal sealed class WritableValue
    {
        public int Value { get; set; }
    }

    internal sealed class GetterOnlyValue
    {
        public int Value { get; }
    }

    internal sealed class ConstructorBoundValue
    {
        public ConstructorBoundValue(int value) => Value = value;

        public int Value { get; }
    }

    internal enum WireState
    {
        Ready = 1,
    }

    internal sealed class EnumEnvelope
    {
        public WireState State { get; set; }
    }

    internal sealed class IgnoredValue
    {
        [JsonIgnore]
        public int Value { get; set; }
    }

    internal sealed class IncludedRequiredValue
    {
        [JsonRequired]
        public int Value { get; set; }
    }

    internal sealed class ConstructorBeforeAddition
    {
        public ConstructorBeforeAddition(int id) => Id = id;

        public int Id { get; }
    }

    internal sealed class ConstructorRequiredAddition
    {
        public ConstructorRequiredAddition(int id, int value)
        {
            Id = id;
            Value = value;
        }

        public int Id { get; }

        [JsonRequired]
        public int Value { get; set; }
    }

    internal sealed class OrdinaryAddition
    {
        public OrdinaryAddition(int id, int value = 0)
        {
            Id = id;
            Value = value;
        }

        public int Id { get; }

        public int Value { get; }
    }

    internal sealed class RecursiveValue
    {
        public int Value { get; set; }

        public RecursiveValue? Next { get; set; }
    }
}

[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(List<int?>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(Dictionary<string, int?>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(HashSet<int>))]
[JsonSerializable(typeof(SortedSet<int>))]
[JsonSerializable(typeof(ComparisonConservatismTests.WritableValue))]
[JsonSerializable(typeof(ComparisonConservatismTests.GetterOnlyValue))]
[JsonSerializable(typeof(ComparisonConservatismTests.ConstructorBoundValue))]
[JsonSerializable(typeof(ComparisonConservatismTests.WireState))]
[JsonSerializable(typeof(ComparisonConservatismTests.EnumEnvelope))]
[JsonSerializable(typeof(ComparisonConservatismTests.IgnoredValue))]
[JsonSerializable(typeof(ComparisonConservatismTests.IncludedRequiredValue))]
[JsonSerializable(typeof(ComparisonConservatismTests.ConstructorBeforeAddition))]
[JsonSerializable(typeof(ComparisonConservatismTests.ConstructorRequiredAddition))]
[JsonSerializable(typeof(ComparisonConservatismTests.OrdinaryAddition))]
[JsonSerializable(typeof(ComparisonConservatismTests.RecursiveValue))]
internal sealed partial class ComparisonSourceContext : JsonSerializerContext
{
}
