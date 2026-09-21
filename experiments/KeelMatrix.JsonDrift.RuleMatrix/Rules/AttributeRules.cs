using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// Declared System.Text.Json serialization facts are enumerated from reflection, carried into the document,
/// and denied unless their exact declaration and argument values are on the measured allowlist.
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
        yield return RedundantConstructorAttribute();
        yield return ConstructorAttributeChangesBinding();
        yield return UnlistedDeclaration(
            "A01.adversarial.attributes-include",
            typeof(IncludeAttributeHolder),
            "a private member is included by [JsonInclude]",
            "JsonIncludeAttribute");
        yield return UnlistedDeclaration(
            "A01.adversarial.attributes-object-creation-handling",
            typeof(ObjectCreationHandlingAttributeHolder),
            "a collection member declares [JsonObjectCreationHandling(Populate)]",
            "JsonObjectCreationHandlingAttribute");
        yield return UnlistedDeclaration(
            "A01.adversarial.attributes-property-order",
            typeof(PropertyOrderAttributeHolder),
            "a member declares [JsonPropertyOrder]",
            "JsonPropertyOrderAttribute");
        yield return UnlistedDeclaration(
            "A01.adversarial.attributes-unmapped-member-handling",
            typeof(UnmappedMemberHandlingAttributeHolder),
            "a type declares [JsonUnmappedMemberHandling(Disallow)]",
            "JsonUnmappedMemberHandlingAttribute");
        yield return StringEnumMemberNameAttribute();
        yield return ExternalRuntimeSerializationAttributes();
        yield return SerializableAttributeIsIrrelevant();
        yield return AcceptedAttributesRecorded();
        yield return SourceGeneratedNumberHandling();
        yield return SourceGeneratedIgnoreCondition();
        yield return SourceGeneratedConstructorAttribute();
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

        JsonDriftReport report = JsonDrift.Compare(changed, JsonDrift.Extract(changed), JsonCompatibility.ReaderBackward);
        bool assertionFailed = false;

        try
        {
            report.AssertCompatible();
        }
        catch (JsonDriftCompatibilityException)
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

    private static CheckOutcome RedundantConstructorAttribute()
    {
        const string id = "A01.adversarial.attributes-constructor-redundant";
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo withoutAttribute = options.GetTypeInfo(typeof(RedundantJsonConstructorWithoutAttribute));
        JsonTypeInfo withAttribute = options.GetTypeInfo(typeof(RedundantJsonConstructorWithAttribute));
        string withoutDocument = ContractCanonicalizer.Canonicalize(withoutAttribute);
        string withDocument = ContractCanonicalizer.Canonicalize(withAttribute);
        string withoutWire = WireProbe.Write(
            new RedundantJsonConstructorWithoutAttribute(7),
            withoutAttribute);
        string withWire = WireProbe.Write(
            new RedundantJsonConstructorWithAttribute(7),
            withAttribute);
        string? reason = ContractClassifier.DescribeUnsupported(withAttribute);
        bool recorded = withDocument.Contains("JsonConstructorAttribute", StringComparison.Ordinal);
        bool absentWithoutAttribute = !withoutDocument.Contains("JsonConstructorAttribute", StringComparison.Ordinal);
        bool denied = string.Equals(
            ContractDocument.RootRule(withDocument),
            RuleIds.UnsupportedAttributeUnlisted,
            StringComparison.Ordinal);
        bool wireUnchanged = string.Equals(withoutWire, withWire, StringComparison.Ordinal);
        bool documentDiffers = !string.Equals(withoutDocument, withDocument, StringComparison.Ordinal);
        bool withoutSupported = ContractDocument.OverallSupported(withoutDocument);
        bool withUnsupported = !ContractDocument.OverallSupported(withDocument);
        bool assertionFailed = AssertUnsupported(id, withAttribute, reason);
        bool passed = recorded && absentWithoutAttribute && denied && wireUnchanged && documentDiffers &&
            withoutSupported && withUnsupported && assertionFailed;

        return Bind(
            Check.Assert(
                id,
                "Declared serialization attributes",
                "a redundant [JsonConstructor] declaration is added to a type with one public parameterized constructor",
                Expected,
                passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" :
                    $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(withUnsupported ? "Unsupported" : "Supported")}, overall={(withUnsupported ? "Unsupported" : "Supported")}",
                $"recorded={recorded}; absentWithoutAttribute={absentWithoutAttribute}; wireUnchanged={wireUnchanged}; " +
                $"documentDiffers={documentDiffers}; withoutWire={withoutWire}; withWire={withWire}; " +
                $"withoutSupported={withoutSupported}; withUnsupported={withUnsupported}; rule={ContractDocument.RootRule(withDocument) ?? "<missing>"}; " +
                $"reason={(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}; assertionFailed={assertionFailed}",
                passed),
            id);
    }

    private static CheckOutcome ConstructorAttributeChangesBinding()
    {
        const string id = "A01.adversarial.attributes-constructor-binding";
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo withoutAttribute = options.GetTypeInfo(typeof(ConstructorBindingWithoutAttribute));
        JsonTypeInfo withAttribute = options.GetTypeInfo(typeof(ConstructorBindingWithAttribute));
        const string document = "{\"Quantity\":7}";
        ReadOutcome withoutRead = WireProbe.Read(document, withoutAttribute);
        ReadOutcome withRead = WireProbe.Read(document, withAttribute);
        string withoutDocument = ContractCanonicalizer.Canonicalize(withoutAttribute);
        string withDocument = ContractCanonicalizer.Canonicalize(withAttribute);
        string? reason = ContractClassifier.DescribeUnsupported(withAttribute);
        bool recorded = withDocument.Contains("JsonConstructorAttribute", StringComparison.Ordinal);
        bool denied = string.Equals(
            ContractDocument.RootRule(withDocument),
            RuleIds.UnsupportedAttributeUnlisted,
            StringComparison.Ordinal);
        bool bindingDiffers = withoutRead.ReboundedDocument == "{\"Quantity\":1}" &&
            withRead.ReboundedDocument == "{\"Quantity\":7}";
        bool documentDiffers = !string.Equals(withoutDocument, withDocument, StringComparison.Ordinal);
        bool withoutUnsupportedForMaterialization =
            !ContractDocument.OverallSupported(withoutDocument) &&
            string.Equals(
                ContractDocument.RootRule(withoutDocument),
                RuleIds.UnsupportedMemberMaterializationUnproven,
                StringComparison.Ordinal);
        bool withUnsupported = !ContractDocument.OverallSupported(withDocument);
        bool assertionFailed = AssertUnsupported(id, withAttribute, reason);
        bool passed = recorded && denied && bindingDiffers && documentDiffers && withoutUnsupportedForMaterialization &&
            withUnsupported && assertionFailed && withoutRead.Fault is null && withRead.Fault is null;

        return Bind(
            Check.Assert(
                id,
                "Declared serialization attributes",
                "[JsonConstructor] selects the parameterized constructor when two public constructors bind the same JSON property differently",
                Expected,
                passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" :
                    $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(withUnsupported ? "Unsupported" : "Supported")}, overall={(withUnsupported ? "Unsupported" : "Supported")}",
                $"recorded={recorded}; bindingDiffers={bindingDiffers}; withoutRead={Check.Describe(withoutRead)}; " +
                $"withRead={Check.Describe(withRead)}; documentDiffers={documentDiffers}; " +
                $"withoutUnsupportedForMaterialization={withoutUnsupportedForMaterialization}; " +
                $"withUnsupported={withUnsupported}; rule={ContractDocument.RootRule(withDocument) ?? "<missing>"}; " +
                $"reason={(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}; assertionFailed={assertionFailed}",
                passed),
            id);
    }

    private static CheckOutcome UnlistedDeclaration(string id, Type changedType, string change, string attributeName)
    {
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo earlier = options.GetTypeInfo(typeof(QuantityInt));
        JsonTypeInfo changed = options.GetTypeInfo(changedType);
        string baselineDocument = ContractCanonicalizer.Canonicalize(earlier);
        string changedDocument = ContractCanonicalizer.Canonicalize(changed);
        string? reason = ContractClassifier.DescribeUnsupported(changed);
        bool recorded = changedDocument.Contains(attributeName, StringComparison.Ordinal);
        bool denied = string.Equals(
            ContractDocument.RootRule(changedDocument),
            RuleIds.UnsupportedAttributeUnlisted,
            StringComparison.Ordinal);
        bool baselineSupported = ContractDocument.OverallSupported(baselineDocument);
        bool changedUnsupported = !ContractDocument.OverallSupported(changedDocument);
        bool documentDiffers = !string.Equals(baselineDocument, changedDocument, StringComparison.Ordinal);
        bool assertionFailed = AssertUnsupported(id, changed, reason);
        bool passed = recorded && denied && baselineSupported && changedUnsupported && documentDiffers && assertionFailed;

        return Bind(
            Check.Assert(
                id,
                "Declared serialization attributes",
                change,
                Expected,
                passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" :
                    $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(changedUnsupported ? "Unsupported" : "Supported")}, overall={(changedUnsupported ? "Unsupported" : "Supported")}",
                $"recorded={recorded}; documentDiffers={documentDiffers}; baselineSupported={baselineSupported}; " +
                $"changedUnsupported={changedUnsupported}; rule={ContractDocument.RootRule(changedDocument) ?? "<missing>"}; " +
                $"reason={(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}; assertionFailed={assertionFailed}",
                passed),
            id);
    }

    private static CheckOutcome StringEnumMemberNameAttribute()
    {
        const string id = "A01.adversarial.attributes-string-enum-member-name";
        JsonSerializerOptions options = JsonContractOptions.Reflection(new JsonStringEnumConverter());
        JsonTypeInfo baseline = options.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo changed = options.GetTypeInfo(typeof(StringEnumMemberNameAttributeHolder));
        string baselineWire = WireProbe.Write(new StateHolderNumeric { State = OrderState.Created }, baseline);
        string changedWire = WireProbe.Write(
            new StringEnumMemberNameAttributeHolder { State = StringEnumMemberNameValue.Created },
            changed);
        string baselineDocument = ContractCanonicalizer.Canonicalize(baseline);
        string changedDocument = ContractCanonicalizer.Canonicalize(changed);
        string? reason = ContractClassifier.DescribeUnsupported(changed);
        bool recorded = changedDocument.Contains("JsonStringEnumMemberNameAttribute", StringComparison.Ordinal) &&
            changedDocument.Contains("created-order", StringComparison.Ordinal);
        bool denied = string.Equals(
            ContractDocument.RootRule(changedDocument),
            RuleIds.UnsupportedAttributeUnlisted,
            StringComparison.Ordinal);
        bool wireDiffers = baselineWire == "{\"State\":\"Created\"}" &&
            changedWire == "{\"State\":\"created-order\"}";
        bool baselineSupported = ContractDocument.OverallSupported(baselineDocument);
        bool changedUnsupported = !ContractDocument.OverallSupported(changedDocument);
        bool documentDiffers = !string.Equals(baselineDocument, changedDocument, StringComparison.Ordinal);
        bool assertionFailed = AssertUnsupported(id, changed, reason);
        bool passed = recorded && denied && wireDiffers && baselineSupported && changedUnsupported &&
            documentDiffers && assertionFailed;

        return Bind(
            Check.Assert(
                id,
                "Declared serialization attributes",
                "a non-JsonAttribute System.Text.Json declaration changes a string enum member's wire name",
                Expected,
                passed ? "metadata=Unsupported, canonical=Unsupported, overall=Unsupported" :
                    $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(changedUnsupported ? "Unsupported" : "Supported")}, overall={(changedUnsupported ? "Unsupported" : "Supported")}",
                $"recorded={recorded}; wireDiffers={wireDiffers}; baselineWire={baselineWire}; changedWire={changedWire}; " +
                $"documentDiffers={documentDiffers}; baselineSupported={baselineSupported}; changedUnsupported={changedUnsupported}; " +
                $"rule={ContractDocument.RootRule(changedDocument) ?? "<missing>"}; reason={(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}; " +
                $"assertionFailed={assertionFailed}",
                passed),
            id);
    }

    private static CheckOutcome ExternalRuntimeSerializationAttributes()
    {
        const string id = "A01.adversarial.attributes-runtime-serialization";
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo baseline = options.GetTypeInfo(typeof(RuntimeSerializationBaselineHolder));
        JsonTypeInfo changed = options.GetTypeInfo(typeof(RuntimeSerializationAttributeHolder));
        JsonTypeInfo generated = TelemetryContext.Default.RuntimeSerializationAttributeHolder;
        string baselineWire = WireProbe.Write(
            new RuntimeSerializationBaselineHolder { Quantity = 7, Omitted = 9 },
            baseline);
        string changedWire = WireProbe.Write(
            new RuntimeSerializationAttributeHolder { Quantity = 7, Omitted = 9 },
            changed);
        string generatedWire = WireProbe.Write(
            new RuntimeSerializationAttributeHolder { Quantity = 7, Omitted = 9 },
            generated);
        string changedDocument = ContractCanonicalizer.Canonicalize(changed);
        string generatedDocument = ContractCanonicalizer.Canonicalize(generated);
        bool noSerializationFact = !changedDocument.Contains("System.Runtime.Serialization", StringComparison.Ordinal);
        bool generatedNoSerializationFact = !generatedDocument.Contains("System.Runtime.Serialization", StringComparison.Ordinal);
        bool wireUnchanged = baselineWire == "{\"Quantity\":7,\"Omitted\":9}" && changedWire == baselineWire && generatedWire == baselineWire;
        bool supported = ContractDocument.OverallSupported(changedDocument) && ContractDocument.OverallSupported(generatedDocument);
        bool passed = noSerializationFact && generatedNoSerializationFact && wireUnchanged && supported;

        return Bind(
            Check.Assert(
                id,
                "External serialization attributes",
                "the default System.Text.Json metadata path measures System.Runtime.Serialization declarations as irrelevant",
                "reflection=Ignored, source-generated=Ignored, wire=Unchanged, overall=Supported",
                passed ? "reflection=Ignored, source-generated=Ignored, wire=Unchanged, overall=Supported" : "external-effect=Observed",
                $"noSerializationFact={noSerializationFact}; generatedNoSerializationFact={generatedNoSerializationFact}; baselineWire={baselineWire}; changedWire={changedWire}; generatedWire={generatedWire}; overallSupported={supported}",
                passed),
            id);
    }

    private static CheckOutcome SerializableAttributeIsIrrelevant()
    {
        const string id = "A01.adversarial.attributes-serializable-irrelevant";
        JsonSerializerOptions options = JsonContractOptions.Reflection();
        JsonTypeInfo baseline = options.GetTypeInfo(typeof(QuantityInt));
        JsonTypeInfo changed = options.GetTypeInfo(typeof(SerializableAttributeHolder));
        string baselineWire = WireProbe.Write(new QuantityInt { Quantity = 7 }, baseline);
        string changedWire = WireProbe.Write(new SerializableAttributeHolder { Quantity = 7 }, changed);
        string changedDocument = ContractCanonicalizer.Canonicalize(changed);
        bool noSerializationFact = !changedDocument.Contains("System.SerializableAttribute", StringComparison.Ordinal);
        bool unchangedWire = baselineWire == "{\"Quantity\":7}" && changedWire == baselineWire;
        bool supported = ContractDocument.OverallSupported(changedDocument);
        bool passed = noSerializationFact && unchangedWire && supported;

        return Bind(
            Check.Assert(
                id,
                "External serialization attributes",
                "[Serializable] is measured as irrelevant to the loaded System.Text.Json contract",
                "recorded=False, wire=Unchanged, overall=Supported",
                passed ? "recorded=False, wire=Unchanged, overall=Supported" : "external-effect=Observed",
                $"noSerializationFact={noSerializationFact}; baselineWire={baselineWire}; changedWire={changedWire}; overallSupported={supported}",
                passed),
            id);
    }

    private static bool AssertUnsupported(string id, JsonTypeInfo contract, string? reason)
    {
        JsonDriftReport report = JsonDrift.Compare(contract, JsonDrift.Extract(contract), JsonCompatibility.ReaderBackward);

        try
        {
            report.AssertCompatible();
            return false;
        }
        catch (JsonDriftCompatibilityException)
        {
            return true;
        }
    }

    private static CheckOutcome AcceptedAttributesRecorded()
    {
        var failures = new List<string>();
        var observed = new List<string>();
        JsonSerializerOptions options = JsonContractOptions.Reflection();

        foreach (Type type in ShippingDeclaredAttributeFacts.MeasuredTypes)
        {
            JsonTypeInfo contract = options.GetTypeInfo(type);
            string? reason = ContractClassifier.DescribeUnsupported(contract);

            if (reason is null)
            {
                IReadOnlyList<RecordedAttributeFact> accepted = ShippingDeclaredAttributeFacts.ReadContractSurface(type);
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

    private static CheckOutcome SourceGeneratedConstructorAttribute()
    {
        const string id = "R13.source-generation.constructor-attribute";
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        string reflectionDocument = ContractCanonicalizer.Canonicalize(
            reflection.GetTypeInfo(typeof(ConstructorBindingWithAttribute)));
        string generatedDocument = ContractCanonicalizer.Canonicalize(TelemetryContext.Default.ConstructorBindingWithAttribute);
        ReadOutcome generatedRead = WireProbe.Read(
            "{\"Quantity\":7}",
            TelemetryContext.Default.ConstructorBindingWithAttribute);
        bool reflectionRecorded = reflectionDocument.Contains("JsonConstructorAttribute", StringComparison.Ordinal);
        bool generatedRecorded = generatedDocument.Contains("JsonConstructorAttribute", StringComparison.Ordinal);
        bool unsupported = !ContractDocument.OverallSupported(reflectionDocument) &&
            !ContractDocument.OverallSupported(generatedDocument) &&
            ContractDocument.RootRule(generatedDocument) == RuleIds.UnsupportedAttributeUnlisted;
        bool bindingObserved = generatedRead.Fault is null && generatedRead.ReboundedDocument == "{\"Quantity\":7}";
        bool passed = reflectionRecorded && generatedRecorded && unsupported && bindingObserved;

        return Bind(
            Check.Assert(
                id,
                "Source-generated metadata",
                "the constructor declaration is recorded from reflection for a source-generated JsonTypeInfo, and source-generated metadata honors its selected constructor",
                "reflection=Recorded, source-generated=Recorded, source-generated=Unsupported, binding=7",
                passed ? "reflection=Recorded, source-generated=Recorded, source-generated=Unsupported, binding=7" : "declared-attributes=MissingOrSupported, binding=unproven",
                $"reflectionRecorded={reflectionRecorded}; generatedRecorded={generatedRecorded}; unsupported={unsupported}; " +
                $"bindingObserved={bindingObserved}; generatedRead={Check.Describe(generatedRead)}",
                passed),
            id);
    }

    private static CheckOutcome Bind(CheckOutcome check, string pathId) =>
        check with
        {
            SourceId = MetadataSourceRules.Id(MetadataSourceKind.DeclaredAttributes),
            PathId = pathId,
        };
}
