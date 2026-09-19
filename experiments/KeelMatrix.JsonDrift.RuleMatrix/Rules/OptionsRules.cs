using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// The serializer option checks. One adversarial check per option family proves that a value the committed
/// checks were not measured under fails closed - the classifier reports the contract unsupported through
/// <c>unsupported.option-unlisted</c>, the canonical document records it as unsupported, and a report that
/// contains it can never be green - while the same contract under the default options stays supported, so the
/// verdict is attributable to the option value. The document check proves that an accepted option value is
/// recorded in the document, so a contract that keeps its shape and changes an option value is a document
/// difference instead of a pair of byte-identical documents.
/// </summary>
internal static class OptionsRules
{
    private const string Expected =
        "metadata=Unsupported, canonical=Unsupported, overall=Unsupported, rule=unsupported.option-unlisted, report=Unsupported";

    /// <summary>One measured option change: the family, the contract it is measured on, and the value.</summary>
    private sealed record OptionChange(
        ContractOptionKind Kind,
        Type ContractType,
        Action<JsonSerializerOptions> Apply);

    /// <summary>One option family and the unlisted values measured against it.</summary>
    private sealed record OptionFamily(ContractOptionKind Kind, IReadOnlyList<OptionChange> Changes);

    private static readonly OptionFamily[] Families =
    {
        new(
            ContractOptionKind.NumberHandling,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.NumberHandling,
                    typeof(QuantityInt),
                    static options => options.NumberHandling = JsonNumberHandling.WriteAsString),
                new OptionChange(
                    ContractOptionKind.NumberHandling,
                    typeof(QuantityInt),
                    static options => options.NumberHandling = JsonNumberHandling.AllowReadingFromString),
            }),
        new(
            ContractOptionKind.ReferenceHandler,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.ReferenceHandler,
                    typeof(OrderEnvelope),
                    static options => options.ReferenceHandler = ReferenceHandler.Preserve),
            }),
        new(
            ContractOptionKind.DefaultIgnoreCondition,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.DefaultIgnoreCondition,
                    typeof(NoteV1),
                    static options => options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault),
            }),
        new(
            ContractOptionKind.UnmappedMemberHandling,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.UnmappedMemberHandling,
                    typeof(NoteV1),
                    static options => options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow),
            }),
        new(
            ContractOptionKind.PropertyNameCaseInsensitive,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.PropertyNameCaseInsensitive,
                    typeof(NoteV1),
                    static options => options.PropertyNameCaseInsensitive = true),
            }),
        new(
            ContractOptionKind.ReadCommentHandling,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.ReadCommentHandling,
                    typeof(NoteV1),
                    static options => options.ReadCommentHandling = JsonCommentHandling.Skip),
            }),
        new(
            ContractOptionKind.AllowTrailingCommas,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.AllowTrailingCommas,
                    typeof(NoteV1),
                    static options => options.AllowTrailingCommas = true),
            }),
        new(
            ContractOptionKind.MaxDepth,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.MaxDepth,
                    typeof(NoteV1),
                    static options => options.MaxDepth = 8),
            }),
        new(
            ContractOptionKind.DictionaryKeyPolicy,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.DictionaryKeyPolicy,
                    typeof(LinesDictionary),
                    static options => options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase),
            }),
        new(
            ContractOptionKind.IgnoreReadOnlyProperties,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.IgnoreReadOnlyProperties,
                    typeof(NoteV1),
                    static options => options.IgnoreReadOnlyProperties = true),
            }),
        new(
            ContractOptionKind.IgnoreReadOnlyFields,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.IgnoreReadOnlyFields,
                    typeof(NoteV1),
                    static options => options.IgnoreReadOnlyFields = true),
            }),
        new(
            ContractOptionKind.PropertyNamingPolicy,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.PropertyNamingPolicy,
                    typeof(NoteV1),
                    static options => options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase),
            }),
        new(
            ContractOptionKind.RespectNullableAnnotations,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.RespectNullableAnnotations,
                    typeof(NoteV1),
                    static options => options.RespectNullableAnnotations = true),
            }),
        new(
            ContractOptionKind.RespectRequiredConstructorParameters,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.RespectRequiredConstructorParameters,
                    typeof(ShipmentEventV1),
                    static options => options.RespectRequiredConstructorParameters = true),
            }),
        new(
            ContractOptionKind.PreferredObjectCreationHandling,
            new[]
            {
                new OptionChange(
                    ContractOptionKind.PreferredObjectCreationHandling,
                    typeof(NoteV1),
                    static options => options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate),
            }),
    };

    public static IEnumerable<CheckOutcome> Run()
    {
        foreach (OptionFamily family in Families)
        {
            yield return Adversarial(family);
        }

        yield return OptionsRecordedInDocument();
    }

    /// <summary>
    /// The check id of one option family. The id is derived from the recorded option identifier, so a family
    /// added to the recorded option set without an adversarial check is visible as a missing entry of the
    /// documented adversarial set.
    /// </summary>
    public static string CheckId(ContractOptionKind kind) =>
        $"A01.adversarial.options-{Kebab(SerializerOptionFacts.Id(kind))}";

    private static CheckOutcome Adversarial(OptionFamily family)
    {
        string checkId = CheckId(family.Kind);
        var measured = new List<string>();
        var observations = new List<string>();
        bool passed = true;

        foreach (OptionChange change in family.Changes)
        {
            JsonSerializerOptions attacked = JsonContractOptions.Reflection();
            change.Apply(attacked);

            string recordedValue = SerializerOptionFacts.Value(change.Kind, attacked);
            bool valueIsUnlisted = !ContractAllowlists.IsAllowlistedOptionValue(change.Kind, recordedValue);
            bool baselineSupported = ContractClassifier.IsSupported(
                JsonContractOptions.Reflection().GetTypeInfo(change.ContractType));

            JsonTypeInfo contract = attacked.GetTypeInfo(change.ContractType);
            string? reason = ContractClassifier.DescribeUnsupported(contract);
            string document = ContractCanonicalizer.Canonicalize(contract);
            string? documentValue = ContractDocument.OptionValue(document, SerializerOptionFacts.Id(change.Kind));
            bool valueRecorded = string.Equals(documentValue, recordedValue, StringComparison.Ordinal);
            bool ruleReported = string.Equals(
                ContractDocument.RootRule(document),
                RuleIds.UnsupportedOptionUnlisted,
                StringComparison.Ordinal);
            bool canonicalSupported = ContractDocument.RootSupported(document);
            bool overallSupported = ContractDocument.OverallSupported(document);

            JsonDriftReport report = JsonDrift.Compare(contract, JsonDrift.Extract(contract), JsonCompatibility.ReaderBackward);

            bool assertionFailed = false;

            try
            {
                report.AssertCompatible();
            }
            catch (JsonDriftCompatibilityException)
            {
                assertionFailed = true;
            }

            bool changePassed =
                valueIsUnlisted &&
                baselineSupported &&
                reason is not null &&
                !canonicalSupported &&
                !overallSupported &&
                ruleReported &&
                valueRecorded &&
                report.Outcome == JsonDriftClassification.Unsupported &&
                assertionFailed;

            passed &= changePassed;
            measured.Add($"{SerializerOptionFacts.Id(change.Kind)}={recordedValue}: {(reason is null ? "Supported" : "Unsupported")}");
            observations.Add(
                $"{SerializerOptionFacts.Id(change.Kind)}={recordedValue}; " +
                $"unlisted={valueIsUnlisted}; baselineSupported={baselineSupported}; canonical={(canonicalSupported ? "Supported" : "Unsupported")}; " +
                $"overall={(overallSupported ? "Supported" : "Unsupported")}; rule={(ContractDocument.RootRule(document) ?? "<missing>")}; " +
                $"documentRecordsValue={valueRecorded}; report={report.Outcome}; assertionFailed={assertionFailed}; " +
                $"reason: {(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}");
        }

        return new CheckOutcome(
            checkId,
            "Serializer option settings",
            $"{SerializerOptionFacts.Id(family.Kind)} is set to a value outside the option allowlist on {TypeShapes.TypeName(family.Changes[0].ContractType)}",
            Expected,
            passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" : string.Join(", ", measured),
            string.Join(" | ", observations),
            passed,
            MetadataSourceRules.Id(MetadataSourceKind.OptionsSettings),
            checkId);
    }

    /// <summary>
    /// A change of an option value the allowlist accepts changes the canonical document, and both contracts
    /// stay supported. This is the property that makes a baseline taken before an option change report drift
    /// instead of reporting a byte-identical document.
    /// </summary>
    private static CheckOutcome OptionsRecordedInDocument()
    {
        string optionId = SerializerOptionFacts.Id(ContractOptionKind.DefaultIgnoreCondition);
        string baseline = ContractCanonicalizer.Canonicalize(
            JsonContractOptions.Reflection().GetTypeInfo(typeof(NoteV1)));
        string changed = ContractCanonicalizer.Canonicalize(
            JsonContractOptions.OmitsNullMembers().GetTypeInfo(typeof(NoteV1)));

        // The same property on the generated pair the R13 rules describe: a source-generated context that
        // declares DefaultIgnoreCondition = WhenWritingNull is no longer byte-identical to the contract
        // recorded under the default reflection options.
        string generatedPairDifference = ContractCanonicalizer.Canonicalize(
            JsonContractOptions.Reflection().GetTypeInfo(typeof(TelemetryBatch)));
        string generatedPairContext = ContractCanonicalizer.Canonicalize(TelemetryContext.Default.TelemetryBatch);
        bool generatedPairDiffers = !string.Equals(generatedPairDifference, generatedPairContext, StringComparison.Ordinal);

        bool identical = string.Equals(baseline, changed, StringComparison.Ordinal);
        string? baselineValue = ContractDocument.OptionValue(baseline, optionId);
        string? changedValue = ContractDocument.OptionValue(changed, optionId);
        bool valuesRecorded =
            string.Equals(baselineValue, ReadValue(JsonContractOptions.Reflection(), ContractOptionKind.DefaultIgnoreCondition), StringComparison.Ordinal) &&
            string.Equals(changedValue, ReadValue(JsonContractOptions.OmitsNullMembers(), ContractOptionKind.DefaultIgnoreCondition), StringComparison.Ordinal);
        bool baselineSupported = ContractDocument.OverallSupported(baseline);
        bool changedSupported = ContractDocument.OverallSupported(changed);
        bool valuesDiffer = !string.Equals(baselineValue, changedValue, StringComparison.Ordinal);
        string generatedPairRecordedValue = ContractDocument.OptionValue(generatedPairContext, optionId) ?? "<missing>";

        return Check.Assert(
            "D08.canonical-document.options-recorded",
            "Canonical document",
            "the same contract is recorded under the default options and under an accepted non-default option value",
            "options=Recorded, documents=Differ, both=Supported, generatedPairDocuments=Differ",
            $"identical={identical}; {optionId}: {baselineValue ?? "<missing>"} versus {changedValue ?? "<missing>"}; " +
            $"generatedPairDocumentsDiffer={generatedPairDiffers}",
            $"baselineOverall={(baselineSupported ? "Supported" : "Unsupported")}; changedOverall={(changedSupported ? "Supported" : "Unsupported")}; " +
            $"generatedPair{optionId}={generatedPairRecordedValue}; " +
            $"baselineBytes={baseline.Length}; changedBytes={changed.Length}",
            !identical && valuesRecorded && valuesDiffer && baselineSupported && changedSupported && generatedPairDiffers);
    }

    private static string ReadValue(JsonSerializerOptions options, ContractOptionKind kind) =>
        SerializerOptionFacts.Value(kind, options);

    private static string Kebab(string identifier)
    {
        var builder = new StringBuilder(identifier.Length);

        foreach (char character in identifier)
        {
            if (char.IsUpper(character))
            {
                builder.Append('-').Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
