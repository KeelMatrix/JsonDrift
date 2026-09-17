using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// F9/F9b: declared JsonAttribute facts are enumerated from reflection, carried into the document, and
/// denied unless their exact declaration and argument values are on the measured allowlist.
/// </summary>
internal static class AttributeRules
{
    private const string Expected =
        "metadata=Unsupported, canonical=Unsupported, overall=Unsupported, rule=unsupported.attribute-unlisted, report=Unsupported";

    public static IEnumerable<CheckOutcome> Run()
    {
        yield return UnlistedNumberHandling(
            "A01.adversarial.attributes-number-handling-type",
            typeof(WriteAsStringNumberType),
            "type-level [JsonNumberHandling(WriteAsString)]");
        yield return UnlistedNumberHandling(
            "A01.adversarial.attributes-number-handling-member",
            typeof(WriteAsStringNumberMember),
            "member-level [JsonNumberHandling(WriteAsString)]");
        yield return UnlistedIgnoreCondition();
        yield return AcceptedAttributesRecorded();
        yield return SourceGeneratedNumberHandling();
        yield return SourceGeneratedIgnoreCondition();
    }

    private static CheckOutcome UnlistedNumberHandling(string id, Type changedType, string change)
    {
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo earlier = options.GetTypeInfo(typeof(QuantityInt));
        JsonTypeInfo changed = options.GetTypeInfo(changedType);
        string earlierWire = WireProbe.Write(new QuantityInt { Quantity = 7 }, earlier);
        object changedValue = changedType == typeof(WriteAsStringNumberType)
            ? new WriteAsStringNumberType { Quantity = 7 }
            : new WriteAsStringNumberMember { Quantity = 7 };
        string changedWire = WireProbe.Write(changedValue, changed);
        ReadOutcome earlierRead = WireProbe.Read(changedWire, earlier);
        string baselineDocument = ContractCanonicalizer.Canonicalize(earlier);
        string changedDocument = ContractCanonicalizer.Canonicalize(changed);
        string? reason = ContractClassifier.DescribeUnsupported(changed);
        string? rootRule = ContractDocument.RootRule(changedDocument);
        bool attributeRecorded = changedDocument.Contains("JsonNumberHandlingAttribute", StringComparison.Ordinal) &&
            changedDocument.Contains("WriteAsString", StringComparison.Ordinal);
        bool denied = string.Equals(rootRule, RuleIds.UnsupportedAttributeUnlisted, StringComparison.Ordinal);
        bool baselineSupported = ContractDocument.OverallSupported(baselineDocument);
        bool changedUnsupported = !ContractDocument.OverallSupported(changedDocument);
        bool documentDiffers = !string.Equals(baselineDocument, changedDocument, StringComparison.Ordinal);
        bool earlierRejected = !earlierRead.Parsed && earlierRead.Fault is null;

        var report = new ContractChangeReport();
        report.AddUnsupported(id, reason is null ? "the contract was reported as supported" : MetadataDiscoverySources.Reason(reason));
        bool assertionFailed = false;

        try
        {
            report.AssertCompatible();
        }
        catch (ContractCheckFailedException)
        {
            assertionFailed = true;
        }

        bool passed =
            attributeRecorded &&
            denied &&
            baselineSupported &&
            changedUnsupported &&
            documentDiffers &&
            earlierRejected &&
            assertionFailed;

        return Bind(
            new CheckOutcome(
                id,
                "Declared serialization attributes",
                change,
                Expected,
                passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" :
                    $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(changedUnsupported ? "Unsupported" : "Supported")}, overall={(changedUnsupported ? "Unsupported" : "Supported")}",
                $"attributeRecorded={attributeRecorded}; documentDiffers={documentDiffers}; baselineWire={earlierWire}; changedWire={changedWire}; " +
                $"earlierReadRejected={earlierRejected}; rule={rootRule ?? "<missing>"}; " +
                $"reason={(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}; assertionFailed={assertionFailed}",
                passed),
            "A01.adversarial.attributes-number-handling-type".Equals(id, StringComparison.Ordinal)
                ? "A01.adversarial.attributes-number-handling-type"
                : "A01.adversarial.attributes-number-handling-member");
    }

    private static CheckOutcome UnlistedIgnoreCondition()
    {
        const string id = "A01.adversarial.attributes-ignore-condition";
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo earlier = options.GetTypeInfo(typeof(QuantityInt));
        JsonTypeInfo changed = options.GetTypeInfo(typeof(DefaultIgnoredMember));
        string baselineDocument = ContractCanonicalizer.Canonicalize(earlier);
        string changedDocument = ContractCanonicalizer.Canonicalize(changed);
        string? reason = ContractClassifier.DescribeUnsupported(changed);
        bool recorded = changedDocument.Contains("JsonIgnoreAttribute", StringComparison.Ordinal) &&
            changedDocument.Contains("WhenWritingDefault", StringComparison.Ordinal);
        bool denied = string.Equals(ContractDocument.RootRule(changedDocument), RuleIds.UnsupportedAttributeUnlisted, StringComparison.Ordinal);
        bool passed = recorded && denied && !string.Equals(baselineDocument, changedDocument, StringComparison.Ordinal);

        return Bind(
            new CheckOutcome(
                id,
                "Declared serialization attributes",
                "member-level [JsonIgnore(Condition = WhenWritingDefault)]",
                Expected,
                passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" :
                    $"metadata={(reason is null ? "Supported" : "Unsupported")}, rule={ContractDocument.RootRule(changedDocument) ?? "<missing>"}",
                $"recorded={recorded}; documentDiffers={!string.Equals(baselineDocument, changedDocument, StringComparison.Ordinal)}; " +
                $"reason={(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}",
                passed),
            id);
    }

    private static CheckOutcome AcceptedAttributesRecorded()
    {
        var failures = new List<string>();
        var observed = new List<string>();
        JsonSerializerOptions options = JsonContractOptions.Reflection();

        foreach (Type type in DeclaredAttributeFacts.MeasuredTypes)
        {
            JsonTypeInfo contract = options.GetTypeInfo(type);
            string? reason = ContractClassifier.DescribeUnsupported(contract);

            if (reason is null)
            {
                IReadOnlyList<RecordedAttributeFact> accepted = DeclaredAttributeFacts.ReadContractSurface(type);
                observed.AddRange(accepted.Select(static attribute => attribute.Display));

                foreach (RecordedAttributeFact attribute in accepted)
                {
                    TraversalInventory.Ledger.RecordAcceptedAttribute(attribute);
                }
            }
            else
            {
                failures.Add($"{TypeShapes.TypeName(type)}: {MetadataDiscoverySources.Reason(reason)}");
            }
        }

        string[] expected = ContractAllowlists.AttributeValueNames.ToArray();
        bool allAccepted = failures.Count == 0 && expected.All(observed.Contains);

        return new CheckOutcome(
            "D08.canonical-document.attributes-recorded",
            "Canonical document",
            "accepted declared JSON attributes are recorded in type/member records and remain supported",
            "attributes=Recorded, allowlisted=Supported",
            allAccepted ? "attributes=Recorded, allowlisted=Supported" : "attributes=MissingOrUnsupported",
            $"expected={expected.Length}; observed={observed.Distinct(StringComparer.Ordinal).Count()}; failures=[{string.Join("; ", failures)}]",
            allAccepted,
            MetadataSourceRules.Id(MetadataSourceKind.DeclaredAttributes),
            "D08.canonical-document.attributes-recorded");
    }

    private static CheckOutcome SourceGeneratedNumberHandling()
    {
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        string reflectionType = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(StrictNumberType)));
        string reflectionMember = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(StrictNumberMember)));
        string generatedType = ContractCanonicalizer.Canonicalize(TelemetryContext.Default.StrictNumberType);
        string generatedMember = ContractCanonicalizer.Canonicalize(TelemetryContext.Default.StrictNumberMember);
        bool reflectionRecorded = reflectionType.Contains("JsonNumberHandlingAttribute", StringComparison.Ordinal) &&
            reflectionMember.Contains("JsonNumberHandlingAttribute", StringComparison.Ordinal);
        bool generatedRecorded = generatedType.Contains("JsonNumberHandlingAttribute", StringComparison.Ordinal) &&
            generatedMember.Contains("JsonNumberHandlingAttribute", StringComparison.Ordinal);
        bool supported = ContractDocument.OverallSupported(reflectionType) && ContractDocument.OverallSupported(generatedType) &&
            ContractDocument.OverallSupported(reflectionMember) && ContractDocument.OverallSupported(generatedMember);
        bool passed = reflectionRecorded && generatedRecorded && supported;

        return Bind(
            Check.Assert(
                "R13.source-generation.number-handling-attributes",
                "Source-generated metadata",
                "type-level and member-level JsonNumberHandling declarations are read from reflection for both reflection and source-generated JsonTypeInfo",
                "reflection=Recorded, source-generated=Recorded, both=Supported",
                passed ? "reflection=Recorded, source-generated=Recorded, both=Supported" : "declared-attributes=MissingOrUnsupported",
                $"reflectionType={reflectionRecorded}; reflectionMember={reflectionRecorded}; sourceGeneratedType={generatedRecorded}; sourceGeneratedMember={generatedRecorded}; supported={supported}",
                passed),
            "R13.source-generation.number-handling-attributes");
    }

    private static CheckOutcome SourceGeneratedIgnoreCondition()
    {
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        string reflectionDocument = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(NeverIgnoredMember)));
        string generatedDocument = ContractCanonicalizer.Canonicalize(TelemetryContext.Default.NeverIgnoredMember);
        bool reflectionRecorded = reflectionDocument.Contains("JsonIgnoreAttribute", StringComparison.Ordinal) &&
            reflectionDocument.Contains("Never", StringComparison.Ordinal);
        bool generatedRecorded = generatedDocument.Contains("JsonIgnoreAttribute", StringComparison.Ordinal) &&
            generatedDocument.Contains("Never", StringComparison.Ordinal);
        bool supported = ContractDocument.OverallSupported(reflectionDocument) && ContractDocument.OverallSupported(generatedDocument);
        bool passed = reflectionRecorded && generatedRecorded && supported;

        return Bind(
            Check.Assert(
                "R13.source-generation.ignore-attributes",
                "Source-generated metadata",
                "member-level JsonIgnore declarations are read from reflection for both reflection and source-generated JsonTypeInfo",
                "reflection=Recorded, source-generated=Recorded, both=Supported",
                passed ? "reflection=Recorded, source-generated=Recorded, both=Supported" : "declared-attributes=MissingOrUnsupported",
                $"reflection={reflectionRecorded}; sourceGenerated={generatedRecorded}; supported={supported}",
                passed),
            "R13.source-generation.ignore-attributes");
    }

    private static CheckOutcome Bind(CheckOutcome check, string pathId) =>
        check with
        {
            SourceId = MetadataSourceRules.Id(MetadataSourceKind.DeclaredAttributes),
            PathId = pathId,
        };
}
