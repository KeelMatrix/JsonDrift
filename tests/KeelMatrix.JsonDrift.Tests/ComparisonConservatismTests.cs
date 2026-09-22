using System.Collections.ObjectModel;
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
    public void EveryFactFamilyPairIsClassifiedAndNewFamilyFailsClosed()
    {
        Assert.Empty(ContractFactInteractions.ValidateCoverage());

        string[] methods = typeof(ComparisonConservatismTests)
            .GetMethods()
            .Select(static method => method.Name)
            .ToArray();
        string[] missingTestWitnesses = ContractFactInteractions.Entries
            .Where(static entry => entry.Witness is not null)
            .Select(static entry => entry.Witness!)
            .Where(static witness => witness.StartsWith("test:", StringComparison.Ordinal))
            .Select(static witness => witness["test:".Length..])
            .Where(witness => !methods.Contains(witness, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingTestWitnesses);

        ContractFactFamily throwaway = new(
            "throwaway-family",
            "throwaway family",
            Array.Empty<string>());
        IReadOnlyList<string> errors = ContractFactInteractions.ValidateCoverage(
            ContractFactInteractions.Families.Append(throwaway).ToArray(),
            ContractFactInteractions.Entries);

        Assert.Equal(ContractFactInteractions.Families.Count, errors.Count(error =>
            error.StartsWith("fact-family interaction is unclassified:", StringComparison.Ordinal) &&
            error.Contains("throwaway-family", StringComparison.Ordinal)));
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
    public void RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints()
    {
        const string EarlierDocument = "{\"account_id\":\"A\"}";

        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo earlier = TypeInfo(typeof(RequiredRenameWithExtensionDataV1), sourceGenerated);
            JsonTypeInfo later = TypeInfo(typeof(RequiredRenameWithExtensionDataV2), sourceGenerated);
            JsonContract earlierContract = JsonDrift.Extract(earlier);

            Assert.True(earlierContract.IsSupported);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(EarlierDocument, later));

            JsonDriftReport report = JsonDrift.Compare(
                later,
                RoundTripBaseline(earlierContract),
                JsonCompatibility.ReaderBackward);

            AssertRejected(report, JsonDriftClassification.Incompatible);
            Assert.Contains(report.Changes, change =>
                change.Path == "root.accountId" &&
                change.RuleId == "R04.requiredness.optional-to-required");

            JsonTypeInfo safeEarlier = TypeInfo(typeof(OptionalRenameV1), sourceGenerated);
            JsonTypeInfo safeLater = TypeInfo(typeof(OptionalRenameV2WithExtensionData), sourceGenerated);
            var rebound = Assert.IsType<OptionalRenameV2WithExtensionData>(
                JsonSerializer.Deserialize(EarlierDocument, safeLater));
            Assert.Equal("A", rebound.Extra["account_id"].GetString());

            JsonDriftReport safeReport = JsonDrift.Compare(
                safeLater,
                RoundTripBaseline(JsonDrift.Extract(safeEarlier)),
                JsonCompatibility.ReaderBackward);
            AssertCompatible(safeReport);
            Assert.Contains(safeReport.Changes, change =>
                change.RuleId == "R03.serialized-name.mitigated-by-extension-data");
        }
    }

    [Fact]
    public void ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds()
    {
        string[] tokens = { "\"not-a-number\"", "true", "{}", "[]", "null" };

        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo earlier = TypeInfo(typeof(ExtensionDataOnly), sourceGenerated);
            JsonTypeInfo later = TypeInfo(typeof(ExtensionDataWithCount), sourceGenerated);
            JsonContract earlierContract = JsonDrift.Extract(earlier);
            Assert.True(earlierContract.IsSupported);

            foreach (string token in tokens)
            {
                using JsonDocument value = JsonDocument.Parse(token);
                var instance = new ExtensionDataOnly
                {
                    Extra = new Dictionary<string, JsonElement>
                    {
                        ["Count"] = value.RootElement.Clone(),
                    },
                };
                string earlierDocument = JsonSerializer.Serialize(instance, earlier);
                Assert.Equal($"{{\"Count\":{token}}}", earlierDocument);
                Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(earlierDocument, later));
            }

            JsonDriftReport report = JsonDrift.Compare(
                later,
                RoundTripBaseline(earlierContract),
                JsonCompatibility.ReaderBackward);
            AssertRejected(report, JsonDriftClassification.Unsupported);
            Assert.Contains(report.Changes, change =>
                change.Path == "root.Count" &&
                change.RuleId == "R01.property-add.extension-data-key-collision");

            JsonTypeInfo laterEnum = TypeInfo(typeof(ExtensionDataWithState), sourceGenerated);
            const string EnumDocument = "{\"State\":\"not-a-state\"}";
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(EnumDocument, laterEnum));
            JsonDriftReport enumReport = JsonDrift.Compare(
                laterEnum,
                RoundTripBaseline(earlierContract),
                JsonCompatibility.ReaderBackward);
            AssertRejected(enumReport, JsonDriftClassification.Unsupported);
            Assert.Contains(enumReport.Changes, change =>
                change.Path == "root.State" &&
                change.RuleId == "R01.property-add.extension-data-key-collision");

            JsonTypeInfo ordinaryEarlier = TypeInfo(typeof(OrdinaryMemberV1), sourceGenerated);
            JsonTypeInfo ordinaryLater = TypeInfo(typeof(OrdinaryMemberV2), sourceGenerated);
            string ordinaryDocument = JsonSerializer.Serialize(new OrdinaryMemberV1 { Id = 7 }, ordinaryEarlier);
            Assert.IsType<OrdinaryMemberV2>(JsonSerializer.Deserialize(ordinaryDocument, ordinaryLater));
            AssertCompatible(JsonDrift.Compare(
                ordinaryLater,
                RoundTripBaseline(JsonDrift.Extract(ordinaryEarlier)),
                JsonCompatibility.ReaderBackward));
        }
    }

    [Fact]
    public void ExtensionDataUnknownDiscriminatorFailsClosedWhenPolymorphismIsAdded()
    {
        const string EarlierDocument = "{\"$type\":\"unknown\"}";

        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo earlier = TypeInfo(typeof(ExtensionDataBeforePolymorphism), sourceGenerated);
            JsonTypeInfo later = TypeInfo(typeof(ExtensionDataWithPolymorphism), sourceGenerated);
            using JsonDocument unknown = JsonDocument.Parse("\"unknown\"");
            var instance = new ExtensionDataBeforePolymorphism
            {
                Extra = new Dictionary<string, JsonElement>
                {
                    ["$type"] = unknown.RootElement.Clone(),
                },
            };

            string earlierDocument = JsonSerializer.Serialize(instance, earlier);
            Assert.Equal(EarlierDocument, earlierDocument);
            JsonException exception = Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize(earlierDocument, later));
            Assert.Contains("Read unrecognized type discriminator id 'unknown'.", exception.Message, StringComparison.Ordinal);

            string directory = Path.Combine(
                Path.GetTempPath(),
                "jsondrift-polymorphism-tests",
                Guid.NewGuid().ToString("N"));
            string baselinePath = Path.Combine(directory, "baseline.json");

            try
            {
                JsonContract written = JsonBaseline.Create(earlier, baselinePath, overwrite: false);
                JsonContract read = JsonBaseline.Read(baselinePath);
                Assert.Equal(written.CanonicalJson, read.CanonicalJson);

                JsonDriftReport report = JsonDrift.Compare(
                    later,
                    baselinePath,
                    JsonCompatibility.ReaderBackward);

                AssertRejected(report, JsonDriftClassification.Unsupported);
                Assert.Contains(report.Changes, change =>
                    change.Path == "root.polymorphism" &&
                    change.RuleId == "R10c.polymorphism.extension-data-discriminator-collision");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ConcreteToAbstractPolymorphicTransitionRequiresADiscriminator()
    {
        const string EarlierDocument = "{\"Name\":\"A\"}";

        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo earlier = TypeInfo(typeof(ConcreteAnimal), sourceGenerated);
            JsonTypeInfo later = TypeInfo(typeof(AbstractAnimal), sourceGenerated);

            string earlierDocument = JsonSerializer.Serialize(new ConcreteAnimal { Name = "A" }, earlier);
            Assert.Equal(EarlierDocument, earlierDocument);

            NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
                JsonSerializer.Deserialize(earlierDocument, later));
            Assert.Contains(
                $"The JSON payload for polymorphic interface or abstract type '{typeof(AbstractAnimal)}' must specify a type discriminator.",
                exception.Message,
                StringComparison.Ordinal);

            string directory = Path.Combine(
                Path.GetTempPath(),
                "jsondrift-polymorphic-materialization-tests",
                Guid.NewGuid().ToString("N"));
            string baselinePath = Path.Combine(directory, "baseline.json");

            try
            {
                JsonContract written = JsonBaseline.Create(earlier, baselinePath, overwrite: false);
                JsonContract read = JsonBaseline.Read(baselinePath);
                Assert.Equal(written.CanonicalJson, read.CanonicalJson);

                JsonDriftReport report = JsonDrift.Compare(
                    later,
                    read,
                    JsonCompatibility.ReaderBackward);

                AssertRejected(report, JsonDriftClassification.Incompatible);
                Assert.Contains(report.Changes, change =>
                    change.Path == "root.polymorphism" &&
                    change.RuleId == "R10d.polymorphism.discriminator-required");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            JsonTypeInfo concreteLater = TypeInfo(typeof(ConcretePolymorphicAnimal), sourceGenerated);
            Assert.IsType<ConcretePolymorphicAnimal>(JsonSerializer.Deserialize(earlierDocument, concreteLater));
            JsonDriftReport concreteReport = Compare(concreteLater, earlier);
            AssertCompatible(concreteReport);
            Assert.Contains(concreteReport.Changes, change =>
                change.Path == "root.polymorphism" &&
                change.RuleId == "R10e.polymorphism.dispatch-added-concrete");
        }
    }

    [Fact]
    public void PlainAbstractAndInterfaceReadersFailClosed()
    {
        const string EarlierDocument = "{\"Name\":\"A\"}";

        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo earlier = TypeInfo(typeof(ConcreteAnimal), sourceGenerated);
            string earlierDocument = JsonSerializer.Serialize(new ConcreteAnimal { Name = "A" }, earlier);
            Assert.Equal(EarlierDocument, earlierDocument);

            foreach (Type laterType in new[] { typeof(PlainAbstractAnimal), typeof(IPlainAnimal) })
            {
                JsonTypeInfo later = TypeInfo(laterType, sourceGenerated);
                Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(earlierDocument, later));

                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "jsondrift-plain-reader-materialization-tests",
                    Guid.NewGuid().ToString("N"));
                string baselinePath = Path.Combine(directory, "baseline.json");

                try
                {
                    JsonContract written = JsonBaseline.Create(earlier, baselinePath, overwrite: false);
                    JsonContract read = JsonBaseline.Read(baselinePath);
                    Assert.Equal(written.CanonicalJson, read.CanonicalJson);

                    JsonDriftReport report = JsonDrift.Compare(
                        later,
                        read,
                        JsonCompatibility.ReaderBackward);

                    AssertRejected(report, JsonDriftClassification.Unsupported);
                    Assert.Contains(report.Changes, change =>
                        change.Path == "root" &&
                        change.RuleId == "unsupported.object-materialization-unproven");
                }
                finally
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    [Fact]
    public void DictionaryMaterializationInteractionsFailClosed()
    {
        foreach (bool sourceGenerated in MetadataPaths())
        {
            JsonTypeInfo earlierRoot = TypeInfo(typeof(Dictionary<string, int>), sourceGenerated);
            JsonTypeInfo laterRoot = TypeInfo(typeof(ReadOnlyDictionary<string, int>), sourceGenerated);
            JsonContract earlierRootContract = JsonDrift.Extract(earlierRoot);
            string rootDocument = JsonSerializer.Serialize(
                new Dictionary<string, int> { ["a"] = 1 },
                earlierRoot);

            Assert.Equal("{\"a\":1}", rootDocument);
            Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(rootDocument, laterRoot));
            JsonDriftReport rootReport = JsonDrift.Compare(
                laterRoot,
                RoundTripBaseline(earlierRootContract),
                JsonCompatibility.ReaderBackward);
            AssertRejected(rootReport, JsonDriftClassification.Unsupported);
            Assert.Contains(rootReport.Changes, change =>
                change.Path == "root" &&
                change.RuleId == "unsupported.dictionary-materialization-unproven");

            JsonTypeInfo earlierNested = TypeInfo(typeof(WritableDictionaryHolder), sourceGenerated);
            JsonTypeInfo laterNested = TypeInfo(typeof(ReadOnlyDictionaryHolder), sourceGenerated);
            JsonContract earlierNestedContract = JsonDrift.Extract(earlierNested);
            string nestedDocument = JsonSerializer.Serialize(
                new WritableDictionaryHolder { Values = { ["a"] = 1 } },
                earlierNested);

            Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize(nestedDocument, laterNested));
            JsonDriftReport nestedReport = JsonDrift.Compare(
                laterNested,
                RoundTripBaseline(earlierNestedContract),
                JsonCompatibility.ReaderBackward);
            AssertRejected(nestedReport, JsonDriftClassification.Unsupported);
            Assert.Contains(nestedReport.Changes, change =>
                change.Path == "root.Values" &&
                change.RuleId == "unsupported.dictionary-materialization-unproven");

            AssertCompatible(JsonDrift.Compare(
                earlierRoot,
                RoundTripBaseline(earlierRootContract),
                JsonCompatibility.ReaderBackward));
            AssertCompatible(JsonDrift.Compare(
                earlierNested,
                RoundTripBaseline(earlierNestedContract),
                JsonCompatibility.ReaderBackward));
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

    internal sealed class RequiredRenameWithExtensionDataV1
    {
        [JsonRequired]
        [JsonPropertyName("account_id")]
        public string? AccountId { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class RequiredRenameWithExtensionDataV2
    {
        [JsonRequired]
        [JsonPropertyName("accountId")]
        public string? AccountId { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class OptionalRenameV1
    {
        [JsonPropertyName("account_id")]
        public string? AccountId { get; set; }
    }

    internal sealed class OptionalRenameV2WithExtensionData
    {
        [JsonPropertyName("accountId")]
        public string? AccountId { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class ExtensionDataOnly
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class ExtensionDataWithCount
    {
        public int Count { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class ExtensionDataWithState
    {
        public CollisionState State { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class ExtensionDataBeforePolymorphism
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ExtensionDataDog), "dog")]
    internal class ExtensionDataWithPolymorphism
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; } = new();
    }

    internal sealed class ExtensionDataDog : ExtensionDataWithPolymorphism
    {
        public int BarkVolume { get; set; }
    }

    internal sealed class ConcreteAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    internal abstract class PlainAbstractAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    internal interface IPlainAnimal
    {
        string Name { get; set; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(AbstractDog), "dog")]
    internal abstract class AbstractAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class AbstractDog : AbstractAnimal
    {
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(ConcreteDog), "dog")]
    internal class ConcretePolymorphicAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class ConcreteDog : ConcretePolymorphicAnimal
    {
    }

    internal enum CollisionState
    {
        Ready = 1,
    }

    internal sealed class OrdinaryMemberV1
    {
        public int Id { get; set; }
    }

    internal sealed class OrdinaryMemberV2
    {
        public int Id { get; set; }

        public int Count { get; set; }
    }

    internal sealed class WritableDictionaryHolder
    {
        public Dictionary<string, int> Values { get; set; } = new();
    }

    internal sealed class ReadOnlyDictionaryHolder
    {
        public ReadOnlyDictionary<string, int> Values { get; set; } =
            new(new Dictionary<string, int>());
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
[JsonSerializable(typeof(ComparisonConservatismTests.RequiredRenameWithExtensionDataV1))]
[JsonSerializable(typeof(ComparisonConservatismTests.RequiredRenameWithExtensionDataV2))]
[JsonSerializable(typeof(ComparisonConservatismTests.OptionalRenameV1))]
[JsonSerializable(typeof(ComparisonConservatismTests.OptionalRenameV2WithExtensionData))]
[JsonSerializable(typeof(ComparisonConservatismTests.ExtensionDataOnly))]
[JsonSerializable(typeof(ComparisonConservatismTests.ExtensionDataWithCount))]
[JsonSerializable(typeof(ComparisonConservatismTests.ExtensionDataWithState))]
[JsonSerializable(typeof(ComparisonConservatismTests.ExtensionDataBeforePolymorphism))]
[JsonSerializable(typeof(ComparisonConservatismTests.ExtensionDataWithPolymorphism))]
[JsonSerializable(typeof(ComparisonConservatismTests.ExtensionDataDog))]
[JsonSerializable(typeof(ComparisonConservatismTests.ConcreteAnimal))]
[JsonSerializable(typeof(ComparisonConservatismTests.PlainAbstractAnimal))]
[JsonSerializable(typeof(ComparisonConservatismTests.IPlainAnimal))]
[JsonSerializable(typeof(ComparisonConservatismTests.AbstractAnimal))]
[JsonSerializable(typeof(ComparisonConservatismTests.AbstractDog))]
[JsonSerializable(typeof(ComparisonConservatismTests.ConcretePolymorphicAnimal))]
[JsonSerializable(typeof(ComparisonConservatismTests.ConcreteDog))]
[JsonSerializable(typeof(ComparisonConservatismTests.OrdinaryMemberV1))]
[JsonSerializable(typeof(ComparisonConservatismTests.OrdinaryMemberV2))]
[JsonSerializable(typeof(ComparisonConservatismTests.WritableDictionaryHolder))]
[JsonSerializable(typeof(ComparisonConservatismTests.ReadOnlyDictionaryHolder))]
[JsonSerializable(typeof(ReadOnlyDictionary<string, int>))]
internal sealed partial class ComparisonSourceContext : JsonSerializerContext
{
}
