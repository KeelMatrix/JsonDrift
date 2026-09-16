using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R14 opaque converter handling, including converters reachable through an element, key, or value type
/// and below the first level of the contract, and the rule that unsupported metadata never produces a
/// green result.
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
            ConverterClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(ReadingV2Opaque)))));

        results.Add(Check.Unsupported(
            "R14.converter.opaque-root",
            "Opaque converter",
            "the root type is serialized by a custom converter",
            ConverterClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(PriceTag)))));

        results.Add(Check.Unsupported(
            "R14.converter.opaque-from-options",
            "Opaque converter",
            "a custom converter is registered on the serializer options for a member type",
            ConverterClassifier.DescribeUnsupported(withConverter.GetTypeInfo(typeof(WalletV2)))));

        results.Add(Check.Supported(
            "R14.converter.known-converter-supported",
            "Opaque converter",
            "a framework string enum converter is applied to an enum member",
            ConverterClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(PriorityHolder)))));

        var customResolverOptions = new JsonSerializerOptions { TypeInfoResolver = new ConverterInjectingResolver() };

        results.Add(Check.Unsupported(
            "R14.converter.custom-metadata-resolver",
            "Opaque converter",
            "a custom metadata resolver replaces a member converter",
            ConverterClassifier.DescribeUnsupported(customResolverOptions.GetTypeInfo(typeof(InvoiceAmounts)))));

        results.Add(Check.Supported(
            "R14.converter.default-resolver-supported",
            "Opaque converter",
            "the metadata comes from the default reflection resolver",
            ConverterClassifier.DescribeUnsupported(reflection.GetTypeInfo(typeof(InvoiceAmounts)))));

        results.Add(Check.Supported(
            "R14.converter.source-generation-resolver-supported",
            "Opaque converter",
            "the metadata comes from a source-generated context",
            ConverterClassifier.DescribeUnsupported(TelemetryContext.Default.TelemetryEvent)));

        AddReachableConverterChecks(results, reflection, withDecimalConverter, withRootConverter);

        JsonTypeInfo later = withConverter.GetTypeInfo(typeof(WalletV2));
        ReadOutcome roundTrip = WireProbe.Across(new WalletV1 { Balance = 9m }, reflection.GetTypeInfo(typeof(WalletV1)), later);
        string? reason = ConverterClassifier.DescribeUnsupported(later);

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
        string? nestedReason = ConverterClassifier.DescribeUnsupported(elementList);

        results.Add(Check.Assert(
            "R14.converter.opaque-nested-round-trip.still-unsupported",
            "Opaque converter",
            "an element type converter produces a lossless round trip one level inside the contract",
            "metadata=Unsupported",
            nestedReason is null ? "metadata=Supported" : "metadata=Unsupported",
            $"roundTrip: {Check.Describe(nestedRoundTrip)} | classification: {(nestedReason is null ? "Supported" : $"Unsupported ({nestedReason})")}",
            nestedRoundTrip.Lossless && nestedRoundTrip.Fault is null && nestedReason is not null));

        var report = new ContractChangeReport();
        report.AddCompatible("R01.property-add.optional", "an optional member was added and earlier members are preserved");
        report.AddUnsupported("R14.converter.opaque-property", reason ?? "unrecognized converter");

        bool assertionFailed = false;
        string assertionMessage = "the assertion did not fail";

        try
        {
            report.AssertCompatible();
        }
        catch (ContractCheckFailedException exception)
        {
            assertionFailed = true;
            assertionMessage = exception.Message;
        }

        results.Add(Check.Assert(
            "U02.unsupported.never-green",
            "Unsupported handling",
            "a run contains one compatible change and one contract that cannot be classified",
            "report=Unsupported and the assertion fails",
            $"report={report.Status}",
            $"status={report.Status}; assert={Check.Truncate(assertionMessage, 200)}",
            report.Status == "Unsupported" && assertionFailed));

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
        results.Add(OpaqueReach(
            "R14.converter.opaque-collection-element",
            "a collection element type declares a custom converter",
            reflection.GetTypeInfo(typeof(ProbeElementList))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-array-element",
            "an array element type declares a custom converter",
            reflection.GetTypeInfo(typeof(ProbeElementArray))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-dictionary-value",
            "a dictionary value type declares a custom converter",
            reflection.GetTypeInfo(typeof(ProbeValueDictionary))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-options-dictionary-value",
            "a converter registered on the options applies to a dictionary value type",
            withDecimalConverter.GetTypeInfo(typeof(ProbeAmountLedger))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-root-from-options",
            "a converter registered on the options applies to the root type",
            withRootConverter.GetTypeInfo(typeof(Money))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-root-element",
            "the root type is a collection whose element type declares a custom converter",
            reflection.GetTypeInfo(typeof(List<ProbeMoney>))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-root-dictionary-value",
            "the root type is a dictionary whose value type is converted by a converter registered on the options",
            withDecimalConverter.GetTypeInfo(typeof(Dictionary<string, decimal>))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-namespace-shadow",
            "a custom converter declares a System.Text.Json namespace",
            reflection.GetTypeInfo(typeof(ShadowConverterHolder))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-nested-element-depth-2",
            "a custom converter is declared on an element type two collection levels below the member",
            reflection.GetTypeInfo(typeof(ProbeNestedElementList))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-nested-element-depth-3",
            "a custom converter is declared on an element type three collection levels below the member",
            reflection.GetTypeInfo(typeof(ProbeDeepElementList))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-nested-combination",
            "a custom converter is declared on a list element inside a dictionary value",
            reflection.GetTypeInfo(typeof(ProbeNestedCombination))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-object-graph-depth-1",
            "a custom converter is declared one member level below the root",
            reflection.GetTypeInfo(typeof(ProbeDepth1))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-object-graph-depth-2",
            "a custom converter is declared two member levels below the root",
            reflection.GetTypeInfo(typeof(ProbeDepth2))));

        results.Add(OpaqueReach(
            "R14.converter.opaque-object-graph-depth-3",
            "a custom converter is declared three member levels below the root",
            reflection.GetTypeInfo(typeof(ProbeDepth3))));

        results.Add(OpaqueReach(
            "R14.converter.traversal-depth-limit",
            "a member nests collections deeper than the classification traversal budget",
            reflection.GetTypeInfo(typeof(ProbeDepthLimitContract))));
    }

    private static CheckOutcome OpaqueReach(string id, string change, JsonTypeInfo contract)
    {
        string? reason = ConverterClassifier.DescribeUnsupported(contract);
        string document = ContractCanonicalizer.Canonicalize(contract);
        bool canonicalSupported = ContractDocument.RootSupported(document);

        var report = new ContractChangeReport();
        report.AddCompatible("R01.property-add.optional", "an optional member was added and earlier members are preserved");
        report.AddUnsupported(id, reason ?? "the contract was reported as supported");

        bool assertionFailed = false;

        try
        {
            report.AssertCompatible();
        }
        catch (ContractCheckFailedException)
        {
            assertionFailed = true;
        }

        return new CheckOutcome(
            id,
            "Opaque converter",
            change,
            "metadata=Unsupported, canonical=Unsupported, report=Unsupported",
            $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(canonicalSupported ? "Supported" : "Unsupported")}, report={report.Status}",
            $"reason: {reason ?? "none"}; assertion failed: {assertionFailed}",
            reason is not null && !canonicalSupported && report.Status == "Unsupported" && assertionFailed);
    }
}
