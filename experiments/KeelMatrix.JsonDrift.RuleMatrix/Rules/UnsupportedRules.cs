using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R14 opaque converter handling on every recorded path, including the paths that reach opaque metadata
/// through an element, key, or value type, through a registered derived type that is itself a collection,
/// dictionary, or polymorphic base, and through metadata below the first level of the contract.
/// </summary>
internal static class UnsupportedRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions withConverter = JsonContractOptions.Reflection(new MoneyConverter());
        JsonSerializerOptions withDecimalConverter = JsonContractOptions.Reflection(new OpaqueDecimalConverter());
        JsonSerializerOptions withRootConverter = JsonContractOptions.Reflection(new MoneyConverter());

        results.Add(Check.Unsupported(
            "R14.converter.opaque-property",
            "Opaque converter",
            "a member is serialized by a custom converter",
            ContractClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(ReadingV2Opaque)))));

        results.Add(Check.Unsupported(
            "R14.converter.opaque-root",
            "Opaque converter",
            "the root type is serialized by a custom converter",
            ContractClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(PriceTag)))));

        results.Add(Check.Unsupported(
            "R14.converter.opaque-from-options",
            "Opaque converter",
            "a custom converter is registered on the serializer options for a member type",
            ContractClassifier.DescribeUnsupported(withConverter.GetTypeInfo(typeof(WalletV2)))));

        results.Add(Check.Supported(
            "R14.converter.known-converter-supported",
            "Opaque converter",
            "a framework string enum converter is applied to an enum member",
            ContractClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(PriorityHolder)))));

        JsonSerializerOptions withEnumConverter = JsonContractOptions.Reflection(new JsonStringEnumConverter());
        JsonTypeInfo enumHolder = withEnumConverter.GetTypeInfo(typeof(StateHolderNumeric));
        string? enumHolderReason = ContractClassifier.DescribeUnsupported(enumHolder);
        string enumHolderDocument = ContractCanonicalizer.Canonicalize(enumHolder);
        bool objectContractSupported = ContractClassifier.IsSupported(withEnumConverter.GetTypeInfo(typeof(InvoiceAmounts)));
        bool recordsStringWireNames = enumHolderDocument.Contains("\"Shipped\": \"Shipped\"", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R14.converter.options-allowlisted-converter-supported",
            "Opaque converter",
            "a framework string enum converter is registered on the options: it applies to the enum member and does not apply to an object contract",
            "metadata=Supported for both contracts and the enum wire names are recorded",
            enumHolderReason is null ? "metadata=Supported" : "metadata=Unsupported",
            $"enumContract: {(enumHolderReason is null ? "Supported" : $"Unsupported ({MetadataDiscoverySources.Reason(enumHolderReason)})")}; " +
            $"objectContract: {(objectContractSupported ? "Supported" : "Unsupported")}; recordsStringWireNames={recordsStringWireNames}",
            enumHolderReason is null && objectContractSupported && recordsStringWireNames));

        var customResolverOptions = new JsonSerializerOptions { TypeInfoResolver = new ConverterInjectingResolver() };

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.custom-metadata-resolver",
            "a custom metadata resolver replaces a member converter",
            MetadataSourceRules.Id(MetadataSourceKind.ResolverChain),
            "R14.converter.custom-metadata-resolver",
            customResolverOptions.GetTypeInfo(typeof(InvoiceAmounts))));

        results.Add(Check.Supported(
            "R14.converter.default-resolver-supported",
            "Opaque converter",
            "the metadata comes from the default reflection resolver",
            ContractClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(InvoiceAmounts)))));

        results.Add(Check.Supported(
            "R14.converter.source-generation-resolver-supported",
            "Opaque converter",
            "the metadata comes from a source-generated context",
            ContractClassifier.DescribeUnsupported(TelemetryContext.Default.TelemetryEvent)));

        AddReachableConverterChecks(results, reflection, withDecimalConverter, withRootConverter);
        AddDerivedShapeChecks(results, reflection);

        JsonTypeInfo later = withConverter.GetTypeInfo(typeof(WalletV2));
        ReadOutcome roundTrip = WireProbe.Across(new WalletV1 { Balance = 9m }, reflection.GetTypeInfo(typeof(WalletV1)), later);
        string? reason = ContractClassifier.DescribeUnsupported(later);

        results.Add(Check.Assert(
            "R14.converter.lossless-round-trip.still-unsupported",
            "Opaque converter",
            "a custom converter happens to produce a lossless round trip",
            "metadata=Unsupported",
            reason is null ? "metadata=Supported" : "metadata=Unsupported",
            $"roundTrip: {Check.Describe(roundTrip)} | classification: {(reason is null ? "Supported" : $"Unsupported ({reason})")}",
            roundTrip.Lossless && roundTrip.Fault is null && reason is not null));

        JsonTypeInfo elementList = reflection.GetTypeInfo(typeof(ProbeElementList));
        ReadOutcome nestedRoundTrip = WireProbe.Across(
            new ProbeElementList { Funds = { new ProbeMoney { Amount = 9m } } },
            elementList,
            elementList);
        string? nestedReason = ContractClassifier.DescribeUnsupported(elementList);

        results.Add(Check.Assert(
            "R14.converter.opaque-nested-round-trip.still-unsupported",
            "Opaque converter",
            "an element type converter produces a lossless round trip one level inside the contract",
            "metadata=Unsupported",
            nestedReason is null ? "metadata=Supported" : "metadata=Unsupported",
            $"roundTrip: {Check.Describe(nestedRoundTrip)} | classification: {(nestedReason is null ? "Supported" : $"Unsupported ({nestedReason})")}",
            nestedRoundTrip.Lossless && nestedRoundTrip.Fault is null && nestedReason is not null));

        JsonDriftReport report = JsonDrift.Compare(later, JsonDrift.Extract(later), JsonCompatibility.ReaderBackward);

        bool assertionFailed = false;
        string assertionMessage = "the assertion did not fail";

        try
        {
            report.AssertCompatible();
        }
        catch (JsonDriftCompatibilityException exception)
        {
            assertionFailed = true;
            assertionMessage = exception.Message;
        }

        results.Add(Check.Assert(
            "U02.unsupported.never-green",
            "Unsupported handling",
            "a run contains one compatible change and one contract that cannot be classified",
            "report=Unsupported and the assertion fails",
            $"report={report.Outcome}",
            $"status={report.Outcome}; assert={Check.Truncate(assertionMessage, 200)}",
            report.Outcome == JsonDriftClassification.Unsupported && assertionFailed));

        return results;
    }

    /// <summary>
    /// Every path that can hide opaque converter metadata is checked as a contract: the classifier reports
    /// it unsupported, the canonical document does not describe it as supported, and a report that contains
    /// it can never be green.
    /// </summary>
    private static void AddReachableConverterChecks(
        List<CheckOutcome> results,
        JsonSerializerOptions reflection,
        JsonSerializerOptions withDecimalConverter,
        JsonSerializerOptions withRootConverter)
    {
        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-collection-element",
            "a collection element type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.EnumerableElementTypes),
            "R14.converter.opaque-collection-element",
            reflection.GetTypeInfo(typeof(ProbeElementList))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-array-element",
            "an array element type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.EnumerableElementTypes),
            "R14.converter.opaque-collection-element",
            reflection.GetTypeInfo(typeof(ProbeElementArray))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-dictionary-value",
            "a dictionary value type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.DictionaryValueTypes),
            "R14.converter.opaque-dictionary-value",
            reflection.GetTypeInfo(typeof(ProbeValueDictionary))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-options-dictionary-value",
            "a converter registered on the options applies to a dictionary value type",
            MetadataSourceRules.Id(MetadataSourceKind.DictionaryValueTypes),
            "R14.converter.opaque-dictionary-value",
            withDecimalConverter.GetTypeInfo(typeof(ProbeAmountLedger))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-dictionary-key",
            "a dictionary key type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.DictionaryKeyTypes),
            "R14.converter.opaque-dictionary-key",
            reflection.GetTypeInfo(typeof(ProbeKeyDictionary))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-root-from-options",
            "a converter registered on the options applies to the root type",
            MetadataSourceRules.Id(MetadataSourceKind.OptionsConverters),
            "R14.converter.opaque-root-from-options",
            withRootConverter.GetTypeInfo(typeof(Money))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-root-element",
            "the root type is a collection whose element type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.EnumerableElementTypes),
            "R14.converter.opaque-collection-element",
            reflection.GetTypeInfo(typeof(List<ProbeMoney>))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-root-dictionary-value",
            "the root type is a dictionary whose value type is converted by a converter registered on the options",
            MetadataSourceRules.Id(MetadataSourceKind.DictionaryValueTypes),
            "R14.converter.opaque-dictionary-value",
            withDecimalConverter.GetTypeInfo(typeof(Dictionary<string, decimal>))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-member-converter",
            "a custom converter declares a System.Text.Json namespace",
            MetadataSourceRules.Id(MetadataSourceKind.MemberConverterAttribute),
            "R14.converter.opaque-member-converter",
            reflection.GetTypeInfo(typeof(ShadowConverterHolder))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-nested-element-depth-2",
            "a custom converter is declared on an element type two collection levels below the member",
            MetadataSourceRules.Id(MetadataSourceKind.EnumerableElementTypes),
            "R14.converter.opaque-collection-element",
            reflection.GetTypeInfo(typeof(ProbeNestedElementList))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-nested-element-depth-3",
            "a custom converter is declared on an element type three collection levels below the member",
            MetadataSourceRules.Id(MetadataSourceKind.EnumerableElementTypes),
            "R14.converter.opaque-collection-element",
            reflection.GetTypeInfo(typeof(ProbeDeepElementList))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-nested-combination",
            "a custom converter is declared on a list element inside a dictionary value",
            MetadataSourceRules.Id(MetadataSourceKind.DictionaryValueTypes),
            "R14.converter.opaque-dictionary-value",
            reflection.GetTypeInfo(typeof(ProbeNestedCombination))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-object-graph-depth-1",
            "a custom converter is declared one member level below the root",
            MetadataSourceRules.Id(MetadataSourceKind.ObjectMembers),
            "R14.converter.opaque-member-type",
            reflection.GetTypeInfo(typeof(ProbeDepth1))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-member-type",
            "a member's declared type carries a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.ObjectMembers),
            "R14.converter.opaque-member-type",
            reflection.GetTypeInfo(typeof(ProbeTypeAttributeHolder))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-type-attribute",
            "a converter attribute is declared on a type reachable through the root type",
            MetadataSourceRules.Id(MetadataSourceKind.TypeConverterAttribute),
            "R14.converter.opaque-type-attribute",
            reflection.GetTypeInfo(typeof(ProbeTypeAttributeHolder))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-object-graph-depth-2",
            "a custom converter is declared two member levels below the root",
            MetadataSourceRules.Id(MetadataSourceKind.ObjectMembers),
            "R14.converter.opaque-member-type",
            reflection.GetTypeInfo(typeof(ProbeDepth2))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-object-graph-depth-3",
            "a custom converter is declared three member levels below the root",
            MetadataSourceRules.Id(MetadataSourceKind.ObjectMembers),
            "R14.converter.opaque-member-type",
            reflection.GetTypeInfo(typeof(ProbeDepth3))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-constructor-parameter",
            "a constructor parameter type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.ConstructorParameters),
            "R14.converter.opaque-constructor-parameter",
            reflection.GetTypeInfo(typeof(ConstructorOpaqueHolder))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-extension-data-value",
            "the value type captured by an extension-data member is converted by a converter registered on the options",
            MetadataSourceRules.Id(MetadataSourceKind.ExtensionData),
            "R14.converter.opaque-extension-data-value",
            JsonContractOptions.Reflection(new OpaqueJsonElementConverter()).GetTypeInfo(typeof(ExtensionDataOpaqueHolder))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-member-custom-converter",
            "the metadata provider assigns a member converter without a converter attribute",
            MetadataSourceRules.Id(MetadataSourceKind.MemberCustomConverter),
            "R14.converter.opaque-member-custom-converter",
            new JsonSerializerOptions { TypeInfoResolver = new ConverterInjectingResolver() }
                .GetTypeInfo(typeof(ResolverInjectedHolder))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-polymorphic-derived-member",
            "a registered derived type carries a member whose type declares a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.PolymorphismDerivedTypes),
            "R14.converter.opaque-polymorphic-derived-member",
            reflection.GetTypeInfo(typeof(PolyRoot))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-polymorphic-options-derived-member",
            "a converter registered on the options applies inside a registered derived type",
            MetadataSourceRules.Id(MetadataSourceKind.PolymorphismDerivedTypes),
            "R14.converter.opaque-polymorphic-derived-member",
            withDecimalConverter.GetTypeInfo(typeof(PolyOptionsRoot))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-polymorphic-nested-depth-2",
            "a registered derived type two derivation levels below a polymorphic member carries an opaque converter",
            MetadataSourceRules.Id(MetadataSourceKind.PolymorphismDerivedTypes),
            "R14.converter.opaque-polymorphic-derived-member",
            reflection.GetTypeInfo(typeof(ProbePolyNested))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.traversal-depth-limit",
            "a member nests collections deeper than the classification traversal budget",
            MetadataSourceRules.Id(MetadataSourceKind.EnumerableElementTypes),
            "R14.converter.opaque-collection-element",
            reflection.GetTypeInfo(typeof(ProbeDepthLimitContract))));
    }

    /// <summary>
    /// A registered derived type is walked by the same recursion as the root contract, so a derived type that
    /// is itself a collection or a dictionary records its element, key, and value types, and a derived type
    /// that is itself a polymorphic base records its own registered derived types. The checks assert both the
    /// unsupported verdict and the recorded shape evidence, and measure the wire change in both directions.
    /// </summary>
    private static void AddDerivedShapeChecks(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        string elementType = TypeShapes.TypeName(typeof(RevOpaqueItem));
        string valueType = TypeShapes.TypeName(typeof(RevOpaqueItem));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-derived-collection-element",
            "a registered derived type that is itself a collection carries an element type with a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.PolymorphismDerivedTypes),
            "R14.converter.opaque-derived-collection-element",
            reflection.GetTypeInfo(typeof(RevOpaqueSequenceBase)),
            documentEvidence: $"\"elementType\": \"{elementType}\""));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-derived-dictionary-value",
            "a registered derived type that is itself a dictionary carries a value type with a custom converter",
            MetadataSourceRules.Id(MetadataSourceKind.PolymorphismDerivedTypes),
            "R14.converter.opaque-derived-dictionary-value",
            reflection.GetTypeInfo(typeof(RevOpaqueMapBase)),
            documentEvidence: $"\"valueType\": \"{valueType}\""));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-derived-polymorphic-base",
            "a registered derived type that is itself a polymorphic base registers a derived type with an opaque member",
            MetadataSourceRules.Id(MetadataSourceKind.PolymorphismDerivedTypes),
            "R14.converter.opaque-derived-polymorphic-base",
            reflection.GetTypeInfo(typeof(PolyOuterBase))));

        results.Add(OpaqueReachCheck.Create(
            "R14.converter.opaque-polymorphic-member-base",
            "a polymorphic member reaches a derived type that is itself a polymorphic base with an opaque member",
            MetadataSourceRules.Id(MetadataSourceKind.ObjectMembers),
            "R14.converter.opaque-polymorphic-member-base",
            reflection.GetTypeInfo(typeof(PolyMemberHolder))));

        JsonTypeInfo earlierSequence = reflection.GetTypeInfo(typeof(RevSequenceBase));
        JsonTypeInfo laterSequence = reflection.GetTypeInfo(typeof(RevOpaqueSequenceBase));

        results.AddRange(Check.Classify(
            "R14.converter.derived-collection.wire-change",
            "Opaque converter",
            "a registered derived type changes from a collection of readable elements to a collection of opaque ones",
            WireProbe.Across(new RevSequence { new RevItem { Value = "kept" } }, earlierSequence, laterSequence),
            readerBackwardCompatible: false,
            WireProbe.Across(new RevOpaqueSequence { new RevOpaqueItem { Value = "kept" } }, laterSequence, earlierSequence),
            writerForwardCompatible: false));

        JsonTypeInfo earlierMap = reflection.GetTypeInfo(typeof(RevMapBase));
        JsonTypeInfo laterMap = reflection.GetTypeInfo(typeof(RevOpaqueMapBase));

        results.AddRange(Check.Classify(
            "R14.converter.derived-dictionary.wire-change",
            "Opaque converter",
            "a registered derived type changes from a dictionary of readable values to a dictionary of opaque ones",
            WireProbe.Across(
                new RevMap { ["kept"] = new RevItem { Value = "kept" } },
                earlierMap,
                laterMap),
            readerBackwardCompatible: false,
            WireProbe.Across(
                new RevOpaqueMap { ["kept"] = new RevOpaqueItem { Value = "kept" } },
                laterMap,
                earlierMap),
            writerForwardCompatible: false));

        string earlierDocument = ContractCanonicalizer.Canonicalize(earlierSequence);
        string laterDocument = ContractCanonicalizer.Canonicalize(laterSequence);
        string earlierMapDocument = ContractCanonicalizer.Canonicalize(earlierMap);
        string laterMapDocument = ContractCanonicalizer.Canonicalize(laterMap);

        bool collectionRecorded =
            !string.Equals(earlierDocument, laterDocument, StringComparison.Ordinal) &&
            earlierDocument.Contains($"\"elementType\": \"{TypeShapes.TypeName(typeof(RevItem))}\"", StringComparison.Ordinal) &&
            laterDocument.Contains($"\"elementType\": \"{TypeShapes.TypeName(typeof(RevOpaqueItem))}\"", StringComparison.Ordinal);

        bool dictionaryRecorded =
            !string.Equals(earlierMapDocument, laterMapDocument, StringComparison.Ordinal) &&
            earlierMapDocument.Contains($"\"valueType\": \"{TypeShapes.TypeName(typeof(RevItem))}\"", StringComparison.Ordinal) &&
            laterMapDocument.Contains($"\"valueType\": \"{TypeShapes.TypeName(typeof(RevOpaqueItem))}\"", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R14.converter.derived-shape.document",
            "Opaque converter",
            "a registered derived type changes between a readable and an opaque collection or dictionary",
            "derivedType=elementAndValueTypesRecorded, documents=differ",
            $"collectionDocumentsDiffer={!string.Equals(earlierDocument, laterDocument, StringComparison.Ordinal)}; " +
            $"dictionaryDocumentsDiffer={!string.Equals(earlierMapDocument, laterMapDocument, StringComparison.Ordinal)}",
            $"collectionElementTypeRecorded={collectionRecorded}; dictionaryValueTypeRecorded={dictionaryRecorded}; " +
            $"earlierCollectionSupported={ContractDocument.OverallSupported(earlierDocument)}; laterCollectionSupported={ContractDocument.OverallSupported(laterDocument)}",
            collectionRecorded && dictionaryRecorded));
    }
}
