using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;

namespace KeelMatrix.JsonDrift.Tests;

public sealed partial class JsonDriftFoundationTests
{
    private static readonly string[] CompleteChangePaths = { "root.Note", "root.Required" };
    private static readonly string[] MultiChangePaths = { "root.Carrier", "root.Secret", "root.Secret" };
    private static readonly string[] MultiChangeRules =
    {
        "R12.binding.constructor-parameter-added",
        "R02.property-materialization",
        "R09.ignore.member-included",
    };

    [Fact]
    public void ExtractsFromReflectionOptionsAndRecordsSupportedContract()
    {
        JsonContract contract = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions());

        Assert.True(contract.IsSupported);
        Assert.Null(contract.UnsupportedReason);
        Assert.Equal(4, contract.FormatVersion);
        Assert.Contains("SimpleEnvelope", contract.RootTypeName, StringComparison.Ordinal);
        Assert.Contains("\n", contract.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractsFromSourceGeneratedTypeInfo()
    {
        JsonContract contract = JsonDrift.Extract(SourceContext.Default.SimpleEnvelope);

        Assert.True(contract.IsSupported);
        Assert.Contains("formatVersion", contract.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalBytesAreUtf8WithoutBomAndRepeatable()
    {
        byte[] first = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).GetCanonicalUtf8();
        byte[] second = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).GetCanonicalUtf8();

        Assert.Equal(first, second);
        Assert.False(first.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', first);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.DoesNotContain("\\", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void RecursiveContractTerminatesWithAReferenceRecord()
    {
        JsonContract contract = JsonDrift.Extract<RecursiveNode>(ReflectionOptions());

        Assert.True(contract.IsSupported);
        Assert.Contains("\"kind\": \"reference\"", contract.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedConverterIsExplicitAndNamesTheFeature()
    {
        JsonSerializerOptions options = ReflectionOptions();
        SecretConverter converter = new();
        options.Converters.Add(converter);

        JsonContract contract = JsonDrift.Extract<SecretEnvelope>(options);

        Assert.False(contract.IsSupported);
        Assert.Equal(0, converter.InvocationCount);
        Assert.NotNull(contract.UnsupportedReason);
        Assert.Contains("converter", contract.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secret", contract.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BaselineCreateUpdateAndReadAreExplicitAndNonMutatingOnRead()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "simple.json");
        JsonTypeInfo<SimpleEnvelope> typeInfo = (JsonTypeInfo<SimpleEnvelope>)ReflectionOptions().GetTypeInfo(typeof(SimpleEnvelope));

        JsonContract created = JsonBaseline.Create(typeInfo, path, overwrite: false);
        byte[] beforeRead = File.ReadAllBytes(path);
        JsonContract read = JsonBaseline.Read(path);

        Assert.Equal(created.GetCanonicalUtf8(), read.GetCanonicalUtf8());
        Assert.Equal(beforeRead, File.ReadAllBytes(path));
        Assert.Throws<IOException>(() => JsonBaseline.Create(typeInfo, path, overwrite: false));
        InvalidOperationException updateError = Assert.Throws<InvalidOperationException>(() => JsonBaseline.Update(typeInfo, path, overwrite: false));
        Assert.Equal("Baseline update requires overwrite: true.", updateError.Message);
        Assert.Throws<IOException>(() => JsonBaseline.Create(typeInfo, path, overwrite: false));
        JsonBaseline.Update(typeInfo, path, overwrite: true);
    }

    [Fact]
    public void CompareReportsOptionalAdditiveChangeAsCompatible()
    {
        JsonContract baseline = JsonDrift.Extract<AdditiveV1>(ReflectionOptions());
        JsonTypeInfo current = ReflectionOptions().GetTypeInfo(typeof(AdditiveV2));

        JsonDriftReport report = JsonDrift.Compare(current, baseline, JsonCompatibility.ReaderBackward);

        JsonDriftChange change = Assert.Single(report.Changes);
        Assert.True(report.IsCompatible);
        Assert.Equal(JsonDriftClassification.Compatible, change.Classification);
        Assert.Equal("R01.property-add.optional", change.RuleId);
        Assert.Equal("root.Note", change.Path);
        report.AssertCompatible();
    }

    [Fact]
    public void CompareReportsRenameAndRequirednessWithStructuredReasons()
    {
        JsonContract renameBaseline = JsonDrift.Extract<RenamedV1>(ReflectionOptions());
        JsonTypeInfo renamedCurrent = ReflectionOptions().GetTypeInfo(typeof(RenamedV2));
        JsonDriftReport renameReport = JsonDrift.Compare(renamedCurrent, renameBaseline, JsonCompatibility.ReaderBackward);

        JsonContract requiredBaseline = JsonDrift.Extract<RequiredV1>(ReflectionOptions());
        JsonTypeInfo requiredCurrent = ReflectionOptions().GetTypeInfo(typeof(RequiredV2));
        JsonDriftReport requiredReport = JsonDrift.Compare(requiredCurrent, requiredBaseline, JsonCompatibility.ReaderBackward);

        Assert.Contains(renameReport.Changes, change =>
            change.RuleId == "R03.serialized-name.json-property-name" &&
            change.Classification == JsonDriftClassification.Incompatible &&
            change.Reason.Contains("serialized member name", StringComparison.Ordinal));
        Assert.Contains(requiredReport.Changes, change =>
            change.RuleId == "R04.requiredness.optional-to-required" &&
            change.Classification == JsonDriftClassification.Incompatible &&
            change.Reason.Contains("requires", StringComparison.Ordinal));
        JsonDriftCompatibilityException exception = Assert.Throws<JsonDriftCompatibilityException>(() => renameReport.AssertCompatible());
        Assert.Same(renameReport, exception.Report);
        Assert.Contains("root.old_name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareKeepsCompleteOrderedChangeSetAndIsDeterministic()
    {
        JsonContract baseline = JsonDrift.Extract<AdditiveV1>(ReflectionOptions());
        JsonTypeInfo current = ReflectionOptions().GetTypeInfo(typeof(AdditiveAndRequiredV2));

        JsonDriftReport first = JsonDrift.Compare(current, baseline, JsonCompatibility.ReaderBackward);
        JsonDriftReport second = JsonDrift.Compare(current, baseline, JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Incompatible, first.Outcome);
        Assert.Equal(CompleteChangePaths, first.Changes.Select(static change => change.Path));
        Assert.Equal(
            first.Changes.Select(static change => change.ToString()),
            second.Changes.Select(static change => change.ToString()));
    }

    [Fact]
    public void CompareReportsIgnoreIncludeAndConstructorBindingInCompleteOrder()
    {
        JsonContract baseline = JsonDrift.Extract<MultiChangeV1>(ReflectionOptions());
        JsonTypeInfo current = ReflectionOptions().GetTypeInfo(typeof(MultiChangeV2));

        JsonDriftReport report = JsonDrift.Compare(current, baseline, JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Compatible, report.Outcome);
        Assert.Equal(MultiChangePaths, report.Changes.Select(static change => change.Path));
        Assert.Equal(MultiChangeRules, report.Changes.Select(static change => change.RuleId));
        Assert.All(report.Changes, static change => Assert.Equal(JsonDriftClassification.Compatible, change.Classification));
    }

    [Fact]
    public void CompareReportsConstructorBindingDefaultEnforcementAndRename()
    {
        JsonContract baseline = JsonDrift.Extract<BindingV1>(ReflectionOptions());

        JsonDriftReport defaultedReport = JsonDrift.Compare(
            ReflectionOptions().GetTypeInfo(typeof(BindingV2Defaulted)),
            baseline,
            JsonCompatibility.ReaderBackward);
        JsonDriftChange defaulted = Assert.Single(defaultedReport.Changes);
        Assert.Equal("R12.binding.constructor-parameter-defaulted", defaulted.RuleId);
        Assert.Equal(JsonDriftClassification.Compatible, defaulted.Classification);

        JsonSerializerOptions strict = ReflectionOptions();
        strict.RespectRequiredConstructorParameters = true;
        JsonDriftReport enforcedReport = JsonDrift.Compare(
            strict.GetTypeInfo(typeof(BindingV2)),
            baseline,
            JsonCompatibility.ReaderBackward);
        Assert.Contains(enforcedReport.Changes, change =>
            change.RuleId == "R12.binding.constructor-parameter-added.enforced" &&
            change.Classification == JsonDriftClassification.Incompatible);

        JsonDriftReport renamedReport = JsonDrift.Compare(
            ReflectionOptions().GetTypeInfo(typeof(BindingRenameV2)),
            JsonDrift.Extract<BindingRenameV1>(ReflectionOptions()),
            JsonCompatibility.ReaderBackward);
        JsonDriftChange renamed = Assert.Single(renamedReport.Changes);
        Assert.Equal("R12.binding.constructor-parameter-renamed", renamed.RuleId);
        Assert.Equal(JsonDriftClassification.Incompatible, renamed.Classification);
    }

    [Fact]
    public void CompareReportsConstructorBoundRemovalAsR02WithBindingContext()
    {
        JsonContract baseline = JsonDrift.Extract<ConstructorBoundRemovalV1>(ReflectionOptions());
        JsonDriftReport report = JsonDrift.Compare(
            ReflectionOptions().GetTypeInfo(typeof(ConstructorBoundRemovalV2)),
            baseline,
            JsonCompatibility.ReaderBackward);

        JsonDriftChange change = Assert.Single(report.Changes);
        Assert.Equal(JsonDriftClassification.Incompatible, report.Outcome);
        Assert.Equal("root.Email", change.Path);
        Assert.Equal("R02.property-removal", change.RuleId);
        Assert.Equal(JsonDriftClassification.Incompatible, change.Classification);
        Assert.Equal(
            "the later contract no longer preserves a member written by the earlier contract; the removed member was bound by a constructor parameter",
            change.Reason);
    }

    [Fact]
    public void CompareDetectsNestedCollectionAndDictionaryContractChangesThroughPublicApi()
    {
        JsonSerializerOptions options = ReflectionOptions();

        JsonDriftReport nested = JsonDrift.Compare(
            options.GetTypeInfo(typeof(NestedEnvelopeV2)),
            WithVersionedTypeNames(
                JsonDrift.Extract<NestedEnvelopeV1>(options),
                (nameof(NestedEnvelopeV1), nameof(NestedEnvelopeV2)),
                (nameof(NestedChildV1), nameof(NestedChildV2))),
            JsonCompatibility.ReaderBackward);
        JsonDriftReport required = JsonDrift.Compare(
            options.GetTypeInfo(typeof(NestedRequiredEnvelopeV2)),
            WithVersionedTypeNames(
                JsonDrift.Extract<NestedRequiredEnvelopeV1>(options),
                (nameof(NestedRequiredEnvelopeV1), nameof(NestedRequiredEnvelopeV2)),
                (nameof(NestedRequiredChildV1), nameof(NestedRequiredChildV2))),
            JsonCompatibility.ReaderBackward);
        JsonDriftReport deep = JsonDrift.Compare(
            options.GetTypeInfo(typeof(DeepEnvelopeV2)),
            WithVersionedTypeNames(
                JsonDrift.Extract<DeepEnvelopeV1>(options),
                (nameof(DeepEnvelopeV1), nameof(DeepEnvelopeV2)),
                (nameof(DeepChildV1), nameof(DeepChildV2)),
                (nameof(DeepLeafV1), nameof(DeepLeafV2))),
            JsonCompatibility.ReaderBackward);
        JsonDriftReport collection = JsonDrift.Compare(
            options.GetTypeInfo(typeof(CollectionEnvelopeV2)),
            WithVersionedTypeNames(
                JsonDrift.Extract<CollectionEnvelopeV1>(options),
                (nameof(CollectionEnvelopeV1), nameof(CollectionEnvelopeV2)),
                (nameof(CollectionItemV1), nameof(CollectionItemV2))),
            JsonCompatibility.ReaderBackward);
        JsonDriftReport dictionary = JsonDrift.Compare(
            options.GetTypeInfo(typeof(DictionaryEnvelopeV2)),
            WithVersionedTypeNames(
                JsonDrift.Extract<DictionaryEnvelopeV1>(options),
                (nameof(DictionaryEnvelopeV1), nameof(DictionaryEnvelopeV2)),
                (nameof(DictionaryItemV1), nameof(DictionaryItemV2))),
            JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Incompatible, nested.Outcome);
        Assert.Contains(nested.Changes, change => change.Path == "root.Child.Value");
        Assert.Equal(JsonDriftClassification.Incompatible, required.Outcome);
        Assert.Contains(required.Changes, change => change.Path == "root.Child.Added");
        Assert.Equal(JsonDriftClassification.Incompatible, deep.Outcome);
        Assert.Contains(deep.Changes, change => change.Path == "root.Child.Leaf.Value");
        Assert.Equal(JsonDriftClassification.Incompatible, collection.Outcome);
        Assert.Contains(collection.Changes, change => change.Path == "root.Items[].Value");
        Assert.Equal(JsonDriftClassification.Incompatible, dictionary.Outcome);
        Assert.Contains(dictionary.Changes, change => change.Path == "root.Values{value}.Value");

        Assert.Throws<JsonDriftCompatibilityException>(() => nested.AssertCompatible());
        Assert.Throws<JsonDriftCompatibilityException>(() => required.AssertCompatible());
        Assert.Throws<JsonDriftCompatibilityException>(() => deep.AssertCompatible());
        Assert.Throws<JsonDriftCompatibilityException>(() => collection.AssertCompatible());
        Assert.Throws<JsonDriftCompatibilityException>(() => dictionary.AssertCompatible());
    }

    [Fact]
    public void CompareDetectsRootScalarTokenChangeAndAssertionFails()
    {
        JsonSerializerOptions options = ReflectionOptions();
        JsonDriftReport report = JsonDrift.Compare(
            options.GetTypeInfo(typeof(string)),
            JsonDrift.Extract(options.GetTypeInfo(typeof(int))),
            JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Incompatible, report.Outcome);
        JsonDriftChange change = Assert.Single(report.Changes);
        Assert.Equal("root", change.Path);
        Assert.Equal("R06.token-kind.number-to-string", change.RuleId);
        Assert.Throws<JsonDriftCompatibilityException>(() => report.AssertCompatible());
    }

    [Fact]
    public void CompareFailsClosedForUnclassifiedRootScalarTransition()
    {
        JsonSerializerOptions options = ReflectionOptions();
        JsonDriftReport report = JsonDrift.Compare(
            options.GetTypeInfo(typeof(Guid)),
            JsonDrift.Extract(options.GetTypeInfo(typeof(DateTime))),
            JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Unsupported, report.Outcome);
        JsonDriftChange change = Assert.Single(report.Changes);
        Assert.Equal("root", change.Path);
        Assert.Equal("R06.token-kind.scalar-unclassified", change.RuleId);
        Assert.Throws<JsonDriftCompatibilityException>(() => report.AssertCompatible());
    }

    [Fact]
    public void CompareRejectsProvenNumericRangeSignednessAndFractionalLoss()
    {
        JsonSerializerOptions options = ReflectionOptions();
        JsonContract intBaseline = JsonDrift.Extract<IntValue>(options);

        JsonDriftReport intToShort = JsonDrift.Compare(
            options.GetTypeInfo(typeof(ShortValue)), intBaseline, JsonCompatibility.ReaderBackward);
        JsonDriftReport intToUnsigned = JsonDrift.Compare(
            options.GetTypeInfo(typeof(UnsignedValue)), intBaseline, JsonCompatibility.ReaderBackward);
        JsonDriftReport decimalToInt = JsonDrift.Compare(
            options.GetTypeInfo(typeof(IntegerValue)), JsonDrift.Extract<DecimalValue>(options), JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Incompatible, intToShort.Outcome);
        Assert.Equal(JsonDriftClassification.Incompatible, intToUnsigned.Outcome);
        Assert.Equal(JsonDriftClassification.Incompatible, decimalToInt.Outcome);
        Assert.All(
            new[] { intToShort, intToUnsigned, decimalToInt },
            report => Assert.Throws<JsonDriftCompatibilityException>(() => report.AssertCompatible()));
    }

    [Fact]
    public void CompareUsesNestedGraphForSourceGeneratedMetadata()
    {
        JsonDriftReport report = JsonDrift.Compare(
            SourceContext.Default.NestedEnvelopeV2,
            WithVersionedTypeNames(
                JsonDrift.Extract(SourceContext.Default.NestedEnvelopeV1),
                (nameof(NestedEnvelopeV1), nameof(NestedEnvelopeV2)),
                (nameof(NestedChildV1), nameof(NestedChildV2))),
            JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Incompatible, report.Outcome);
        Assert.Contains(report.Changes, change => change.Path == "root.Child.Value");
        Assert.Throws<JsonDriftCompatibilityException>(() => report.AssertCompatible());
    }

    [Fact]
    public void CompareFailsClosedForUnsupportedConverterWithoutExecutingIt()
    {
        JsonSerializerOptions options = ReflectionOptions();
        SecretConverter converter = new();
        options.Converters.Add(converter);
        JsonTypeInfo current = options.GetTypeInfo(typeof(SecretEnvelope));
        JsonContract baseline = JsonDrift.Extract(current);

        JsonDriftReport report = JsonDrift.Compare(current, baseline, JsonCompatibility.ReaderBackward);

        Assert.Equal(JsonDriftClassification.Unsupported, report.Outcome);
        Assert.NotEmpty(report.Changes);
        Assert.Throws<JsonDriftCompatibilityException>(() => report.AssertCompatible());
        Assert.Equal(0, converter.InvocationCount);
    }

    [Fact]
    public void ComparePathUsesBoundedBaselineValidationAndRecursiveGraphsTerminate()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "baseline.json");
        JsonBaseline.Create<SimpleEnvelope>(ReflectionOptions(), path, overwrite: false);

        JsonDriftReport report = JsonDrift.Compare<SimpleEnvelope>(ReflectionOptions(), path, JsonCompatibility.ReaderBackward);
        Assert.True(report.IsCompatible);
        Assert.Empty(report.Changes);

        File.WriteAllText(path, "{\"formatVersion\":5}\n", new UTF8Encoding(false));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            JsonDrift.Compare<SimpleEnvelope>(ReflectionOptions(), path, JsonCompatibility.ReaderBackward));
        Assert.Contains("newer than supported", error.Message, StringComparison.Ordinal);

        JsonTypeInfo recursive = ReflectionOptions().GetTypeInfo(typeof(RecursiveNode));
        JsonDriftReport recursiveReport = JsonDrift.Compare(recursive, JsonDrift.Extract(recursive), JsonCompatibility.ReaderBackward);
        Assert.True(recursiveReport.IsCompatible);
        Assert.Empty(recursiveReport.Changes);
    }

    [Fact]
    public void ReadRejectsMalformedForeignOldFutureDepthAndOversizedDocuments()
    {
        using TemporaryDirectory directory = new();

        AssertReadFailure(directory, "malformed.json", "not-json\n", "Baseline is malformed JSON.");
        AssertReadFailure(directory, "foreign.json", "{\"name\":\"other\"}\n", "Baseline is not a JsonDrift canonical contract: missing formatVersion.");
        AssertReadFailure(directory, "old.json", "{\"formatVersion\":0}\n", "Baseline format version 0 is not supported.");
        AssertReadFailure(directory, "future.json", "{\"formatVersion\":5}\n", "Baseline format version 5 is newer than supported version 4.");

        string deep = new string('[', 12) + new string(']', 12) + "\n";
        string depthPath = Path.Combine(directory.Path, "depth.json");
        File.WriteAllText(depthPath, deep, new UTF8Encoding(false));
        InvalidDataException depthError = Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(depthPath, new JsonBaselineLimits(maximumDepth: 3)));
        Assert.Equal("Baseline exceeds the maximum nesting depth of 3.", depthError.Message);

        string sizePath = Path.Combine(directory.Path, "size.json");
        File.WriteAllText(sizePath, "{}\n", new UTF8Encoding(false));
        InvalidDataException sizeError = Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(sizePath, new JsonBaselineLimits(maximumBytes: 2)));
        Assert.Equal("Baseline exceeds the maximum size of 2 bytes.", sizeError.Message);
    }

    [Fact]
    public void ReadRejectsTruncatedIncompleteUnknownDuplicateAndInconsistentDocuments()
    {
        using TemporaryDirectory directory = new();
        string canonical = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson;

        string truncatedPath = Path.Combine(directory.Path, "truncated.json");
        File.WriteAllText(truncatedPath, canonical[..^2] + "\n", new UTF8Encoding(false));
        Assert.IsType<InvalidDataException>(Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(truncatedPath)));

        AssertReadFailure(
            directory,
            "missing-field.json",
            canonical.Replace("    \"path\": \"root\",\n", string.Empty, StringComparison.Ordinal),
            "Baseline root is missing required property 'path'.");

        AssertReadFailure(
            directory,
            "unknown-field.json",
            canonical.Replace("  \"overallSupported\": true\n", "  \"unknown\": true,\n  \"overallSupported\": true\n", StringComparison.Ordinal),
            "Baseline top-level contains an unknown property.");

        AssertReadFailure(
            directory,
            "duplicate-member.json",
            canonical.Replace("  \"formatVersion\": 4,\n", "  \"formatVersion\": 4,\n  \"formatVersion\": 4,\n", StringComparison.Ordinal),
            "Baseline contains duplicate JSON members.");

        AssertReadFailure(
            directory,
            "false-overall-support.json",
            canonical.Replace("  \"overallSupported\": true\n", "  \"overallSupported\": false\n", StringComparison.Ordinal),
            "Baseline has inconsistent overallSupported and root support state.");

        AssertReadFailure(
            directory,
            "supported-with-reason.json",
            canonical.Replace("    \"supported\": true,\n    \"declaredAttributes\"", "    \"supported\": true,\n    \"reason\": \"forged\",\n    \"declaredAttributes\"", StringComparison.Ordinal),
            "Baseline root has inconsistent support and reason state.");
    }

    [Fact]
    public void ReadAcceptsDocumentExactlyAtTheConfiguredByteLimitAndRejectsOneByteOver()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "bounded.json");
        byte[] canonical = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).GetCanonicalUtf8();
        File.WriteAllBytes(path, canonical);

        JsonContract read = JsonBaseline.Read(path, new JsonBaselineLimits(maximumBytes: canonical.Length));
        Assert.Equal(canonical, read.GetCanonicalUtf8());

        File.WriteAllBytes(path, canonical.Concat(new byte[] { (byte)' ' }).ToArray());
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            JsonBaseline.Read(path, new JsonBaselineLimits(maximumBytes: canonical.Length)));
        Assert.Equal($"Baseline exceeds the maximum size of {canonical.Length:N0} bytes.", error.Message);
    }

    [Fact]
    public async Task ConcurrentNonOverwritingCreatesHaveExactlyOneWinner()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "race.json");
        JsonTypeInfo<SimpleEnvelope> typeInfo = (JsonTypeInfo<SimpleEnvelope>)ReflectionOptions().GetTypeInfo(typeof(SimpleEnvelope));

        Task<bool>[] attempts = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    JsonBaseline.Create(typeInfo, path, overwrite: false);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }))
            .ToArray();

        bool[] winners = await Task.WhenAll(attempts);

        Assert.Equal(1, winners.Count(static winner => winner));
        Assert.True(File.Exists(path));
        Assert.Equal(JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson, JsonBaseline.Read(path).CanonicalJson);
    }

    [Fact]
    public async Task UpdateThatLosesAConcurrentDeleteFailsWithoutCreatingABaseline()
    {
        using TemporaryDirectory directory = new();
        JsonTypeInfo<SimpleEnvelope> typeInfo = (JsonTypeInfo<SimpleEnvelope>)ReflectionOptions().GetTypeInfo(typeof(SimpleEnvelope));
        bool observedDeleteWinner = false;

        for (int attempt = 0; attempt < 64 && !observedDeleteWinner; attempt++)
        {
            string path = Path.Combine(directory.Path, $"update-race-{attempt}.json");
            JsonBaseline.Create(typeInfo, path, overwrite: false);
            using var start = new ManualResetEventSlim(false);

            Task<Exception?> update = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    JsonBaseline.Update(typeInfo, path, overwrite: true);
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            Task delete = Task.Run(() =>
            {
                start.Wait();
                File.Delete(path);
            });

            start.Set();
            Exception? updateError = await update;
            await delete;

            if (updateError is IOException)
            {
                observedDeleteWinner = true;
                Assert.False(File.Exists(path));
                Assert.Contains("atomically update baseline", updateError.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Null(updateError);
                File.Delete(path);
            }
        }

        Assert.True(observedDeleteWinner, "The race did not exercise the concurrent-delete loser path.");
    }

    [Fact]
    public void ReadRejectsBomAndCrlfBaselines()
    {
        using TemporaryDirectory directory = new();
        string bomPath = Path.Combine(directory.Path, "bom.json");
        File.WriteAllBytes(bomPath, new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes("{}\n")).ToArray());
        Assert.Equal("Baseline must be UTF-8 without a byte order mark.", Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(bomPath)).Message);

        string crlfPath = Path.Combine(directory.Path, "crlf.json");
        File.WriteAllText(crlfPath, "{}\r\n", new UTF8Encoding(false));
        Assert.Equal("Baseline must use LF line endings.", Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(crlfPath)).Message);
    }

    [Fact]
    public void ExtractionIsSafeUnderConcurrency()
    {
        string expected = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson;
        string[] results = new string[32];

        Parallel.For(0, results.Length, index => results[index] = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson);

        Assert.All(results, result => Assert.Equal(expected, result));
    }

    [Fact]
    public void PublicTypesAndMembersHaveXmlDocumentation()
    {
        string xmlPath = Path.ChangeExtension(typeof(JsonDrift).Assembly.Location, ".xml");
        Assert.True(File.Exists(xmlPath), $"Missing XML documentation file: {xmlPath}");
        XDocument xml = XDocument.Load(xmlPath);
        HashSet<string> documented = xml.Descendants("member")
            .Select(static element => (string?)element.Attribute("name"))
            .Where(static name => name is not null)
            .Select(static name => name!)
            .ToHashSet(StringComparer.Ordinal);

        Type[] publicTypes = typeof(JsonDrift).Assembly.GetExportedTypes()
            .Where(static type => type.Namespace == "KeelMatrix.JsonDrift")
            .ToArray();
        Assert.NotEmpty(publicTypes);
        foreach (Type type in publicTypes)
        {
            Assert.Contains($"T:{type.FullName}", documented);
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (member is MethodInfo { IsSpecialName: true } ||
                    member is not (MethodInfo or PropertyInfo or ConstructorInfo or FieldInfo) ||
                    member.Name == "value__")
                {
                    continue;
                }

                string prefix = member switch
                {
                    ConstructorInfo => "M:",
                    MethodInfo => "M:",
                    PropertyInfo => "P:",
                    FieldInfo => "F:",
                    _ => string.Empty,
                };
                string memberName = member is ConstructorInfo ? "#ctor" : member.Name;
                Assert.Contains(documented, name => name.StartsWith(prefix + type.FullName + "." + memberName, StringComparison.Ordinal));
            }
        }
    }

    private static JsonSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static void AssertReadFailure(TemporaryDirectory directory, string fileName, string content, string message)
    {
        string path = Path.Combine(directory.Path, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(path));
        Assert.Equal(message, error.Message);
    }

    private static JsonContract WithVersionedTypeNames(JsonContract contract, params (string Earlier, string Later)[] replacements)
    {
        string canonical = contract.CanonicalJson;
        foreach ((string earlier, string later) in replacements)
        {
            canonical = canonical.Replace(earlier, later, StringComparison.Ordinal);
        }

        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "baseline.json");
        File.WriteAllText(path, canonical, new UTF8Encoding(false));
        return JsonBaseline.Read(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jsondrift-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class Secret
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class SecretEnvelope
    {
        public Secret Data { get; set; } = new();
    }

    private sealed class SecretConverter : JsonConverter<Secret>
    {
        public int InvocationCount { get; private set; }

        public override Secret? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            InvocationCount++;
            return new() { Value = reader.GetString() ?? string.Empty };
        }

        public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options)
        {
            InvocationCount++;
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class SimpleEnvelope
    {
        public int Id { get; set; }

        public string? Note { get; set; }
    }

    private sealed class AdditiveV1
    {
        public int Id { get; set; }
    }

    private sealed class AdditiveV2
    {
        public int Id { get; set; }

        public string? Note { get; set; }
    }

    private sealed class AdditiveAndRequiredV2
    {
        public int Id { get; set; }

        public string? Note { get; set; }

        [JsonRequired]
        public string Required { get; set; } = string.Empty;
    }

    private sealed class RenamedV1
    {
        [JsonPropertyName("old_name")]
        public string? Name { get; set; }
    }

    private sealed class RenamedV2
    {
        [JsonPropertyName("new_name")]
        public string? Name { get; set; }
    }

    private sealed class RequiredV1
    {
        public string? Tracking { get; set; }
    }

    private sealed class RequiredV2
    {
        [JsonRequired]
        public string? Tracking { get; set; }
    }

    private sealed class MultiChangeV1
    {
        public MultiChangeV1(string id) => Id = id;

        public string Id { get; }

        [JsonIgnore]
        public string Secret { get; set; } = string.Empty;
    }

    private sealed class MultiChangeV2
    {
        public MultiChangeV2(string id, string carrier)
        {
            Id = id;
            Carrier = carrier;
        }

        public string Id { get; }

        public string Carrier { get; }

        public string Secret { get; set; } = string.Empty;
    }

    private sealed record BindingV1(string Id);

    private sealed record BindingV2(string Id, string Carrier);

    private sealed record BindingV2Defaulted(string Id, string Carrier = "unspecified");

    private sealed record BindingRenameV1(decimal Value);

    private sealed record BindingRenameV2(decimal Amount);

    private sealed record ConstructorBoundRemovalV1(string Name, string Email);

    private sealed record ConstructorBoundRemovalV2(string Name);

    private sealed class RecursiveNode
    {
        public string? Value { get; set; }

        public RecursiveNode? Next { get; set; }
    }

    private sealed class NestedEnvelopeV1
    {
        public NestedChildV1 Child { get; set; } = new();
    }

    private sealed class NestedChildV1
    {
        public int Value { get; set; }
    }

    private sealed class NestedEnvelopeV2
    {
        public NestedChildV2 Child { get; set; } = new();
    }

    private sealed class NestedChildV2
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class NestedRequiredEnvelopeV1
    {
        public NestedRequiredChildV1 Child { get; set; } = new();
    }

    private sealed class NestedRequiredChildV1
    {
        public int Value { get; set; }
    }

    private sealed class NestedRequiredEnvelopeV2
    {
        public NestedRequiredChildV2 Child { get; set; } = new();
    }

    private sealed class NestedRequiredChildV2
    {
        public int Value { get; set; }

        [JsonRequired]
        public string Added { get; set; } = string.Empty;
    }

    private sealed class DeepEnvelopeV1
    {
        public DeepChildV1 Child { get; set; } = new();
    }

    private sealed class DeepChildV1
    {
        public DeepLeafV1 Leaf { get; set; } = new();
    }

    private sealed class DeepLeafV1
    {
        public int Value { get; set; }
    }

    private sealed class DeepEnvelopeV2
    {
        public DeepChildV2 Child { get; set; } = new();
    }

    private sealed class DeepChildV2
    {
        public DeepLeafV2 Leaf { get; set; } = new();
    }

    private sealed class DeepLeafV2
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class CollectionEnvelopeV1
    {
        public List<CollectionItemV1> Items { get; set; } = new();
    }

    private sealed class CollectionItemV1
    {
        public int Value { get; set; }
    }

    private sealed class CollectionEnvelopeV2
    {
        public List<CollectionItemV2> Items { get; set; } = new();
    }

    private sealed class CollectionItemV2
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class DictionaryEnvelopeV1
    {
        public Dictionary<string, DictionaryItemV1> Values { get; set; } = new();
    }

    private sealed class DictionaryItemV1
    {
        public int Value { get; set; }
    }

    private sealed class DictionaryEnvelopeV2
    {
        public Dictionary<string, DictionaryItemV2> Values { get; set; } = new();
    }

    private sealed class DictionaryItemV2
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class IntValue
    {
        public int Value { get; set; }
    }

    private sealed class ShortValue
    {
        public short Value { get; set; }
    }

    private sealed class UnsignedValue
    {
        public uint Value { get; set; }
    }

    private sealed class DecimalValue
    {
        public decimal Value { get; set; }
    }

    private sealed class IntegerValue
    {
        public int Value { get; set; }
    }

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(SimpleEnvelope))]
    [JsonSerializable(typeof(NestedEnvelopeV1))]
    [JsonSerializable(typeof(NestedEnvelopeV2))]
    private sealed partial class SourceContext : JsonSerializerContext
    {
    }
}
