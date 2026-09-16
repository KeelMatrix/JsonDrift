using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R15 measurement-harness behavior: a probe that cannot run fails a check instead of terminating the run,
/// an unsupported member is never resolved any further, and the document comparison the probes rely on
/// compares numbers by value.
/// </summary>
internal static class HarnessRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        var injectingOptions = new JsonSerializerOptions { TypeInfoResolver = new ConverterInjectingResolver() };

        AddProbeExceptionChecks(results, reflection, injectingOptions);
        AddCanonicalDocumentChecks(results, reflection);
        AddAggregateSupportChecks(results, reflection);
        AddComparisonChecks(results);

        return results;
    }

    private static void AddProbeExceptionChecks(
        List<CheckOutcome> results,
        JsonSerializerOptions reflection,
        JsonSerializerOptions injectingOptions)
    {
        JsonTypeInfo injected = injectingOptions.GetTypeInfo(typeof(InvoiceAmounts));
        JsonTypeInfo plain = reflection.GetTypeInfo(typeof(InvoiceAmounts));

        ReadOutcome writeFault = WireProbe.Across(new InvoiceAmounts { AmountDue = 12.5m }, injected, plain);
        ReadOutcome readFault = WireProbe.Read("{\"AmountDue\":12.5}", injected);

        CheckOutcome compatibleExpectation = Check.Compatible(
            "R15.probe.unexpected-exception.compatible",
            "Measurement harness",
            "a probe that cannot run is treated as a compatible reading",
            writeFault);

        CheckOutcome incompatibleExpectation = Check.Incompatible(
            "R15.probe.unexpected-exception.incompatible",
            "Measurement harness",
            "a probe that cannot run is treated as an incompatible reading",
            writeFault);

        results.Add(Check.Assert(
            "R15.probe.unexpected-exception",
            "Measurement harness",
            "a resolver-injected converter raises an exception the harness does not expect",
            "probe=fault and no check built on the probe passes",
            $"writeFault={(writeFault.Fault is null ? "none" : "recorded")}; readFault={(readFault.Fault is null ? "none" : "recorded")}; compatibleExpectation={(compatibleExpectation.Passed ? "passed" : "failed")}; incompatibleExpectation={(incompatibleExpectation.Passed ? "passed" : "failed")}",
            $"write: {Check.Truncate(writeFault.Fault, 160)}; read: {Check.Truncate(readFault.Fault, 160)}",
            writeFault.Fault is not null &&
            readFault.Fault is not null &&
            !compatibleExpectation.Passed &&
            !incompatibleExpectation.Passed));

        ReadOutcome rejected = WireProbe.Read("{\"Id\":1}", reflection.GetTypeInfo(typeof(ShipmentRequired)));

        CheckOutcome rejectedExpectation = Check.Incompatible(
            "R15.probe.contract-rejection.incompatible",
            "Measurement harness",
            "a contract rejects a document because a member is required",
            rejected);

        results.Add(Check.Assert(
            "R15.probe.contract-rejection",
            "Measurement harness",
            "a contract rejects a document because a required member is missing",
            "probe=rejection, no fault, and the incompatible expectation holds",
            $"parsed={rejected.Parsed}; fault={(rejected.Fault is null ? "none" : "recorded")}; incompatibleExpectation={(rejectedExpectation.Passed ? "passed" : "failed")}",
            $"error: {Check.Truncate(rejected.Failure, 160)}",
            rejected.Fault is null && !rejected.Parsed && rejectedExpectation.Passed));
    }

    private static void AddCanonicalDocumentChecks(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        string document = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(MixedOpaqueContract)));

        bool opaqueSupported = ContractDocument.MemberSupported(document, "Reading");
        bool opaqueHasShape = ContractDocument.MemberHasShape(document, "Reading");
        bool classifiableSupported = ContractDocument.MemberSupported(document, "Amount");
        bool classifiableHasShape = ContractDocument.MemberHasShape(document, "Amount");

        results.Add(Check.Assert(
            "R15.canonical-document.unsupported-member-shape-skipped",
            "Measurement harness",
            "a contract has one classifiable complex member and one member whose converter is opaque",
            "unsupported member=no shape; classifiable member=shape recorded",
            $"opaqueMember: supported={opaqueSupported}, hasShape={opaqueHasShape}; classifiableMember: supported={classifiableSupported}, hasShape={classifiableHasShape}",
            $"documentLength={document.Length}",
            !opaqueSupported && !opaqueHasShape && classifiableSupported && classifiableHasShape));
    }

    /// <summary>
    /// The aggregate support state is part of the document, so a report layer never has to infer that a
    /// contract is safe from the root flag while a nested contract is unsupported.
    /// </summary>
    private static void AddAggregateSupportChecks(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        string opaque = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(MixedOpaqueContract)));
        string supported = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(OrderEnvelope)));

        bool opaqueAggregate = ContractDocument.OverallSupported(opaque);
        bool supportedAggregate = ContractDocument.OverallSupported(supported);

        results.Add(Check.Assert(
            "R15.canonical-document.aggregate-support-state",
            "Measurement harness",
            "one contract has an unsupported member and one contract is fully classifiable",
            "unsupported contract overall=Unsupported; classifiable contract overall=Supported",
            $"mixedContract: root={(ContractDocument.RootSupported(opaque) ? "Supported" : "Unsupported")}, overall={(opaqueAggregate ? "Supported" : "Unsupported")}; classifiableContract: overall={(supportedAggregate ? "Supported" : "Unsupported")}",
            $"mixedDocumentLength={opaque.Length}; classifiableDocumentLength={supported.Length}",
            !opaqueAggregate && supportedAggregate));

        string numeric = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(ShiftRootNumeric)));
        string text = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(ShiftRootText)));

        results.Add(Check.Assert(
            "R10.polymorphism.derived-member.document",
            "Polymorphic type metadata change",
            "a registered derived type keeps its discriminator and member names but changes one member's token kind",
            "canonical documents differ and both contracts are supported",
            $"identical={string.Equals(numeric, text, StringComparison.Ordinal)}",
            $"earlierRecordsNumericAmount={numeric.Contains("\"tokenKind\": \"number\"", StringComparison.Ordinal)}; " +
            $"laterRecordsStringAmount={text.Contains("\"tokenKind\": \"string\"", StringComparison.Ordinal)}; " +
            $"earlierOverall={(ContractDocument.OverallSupported(numeric) ? "Supported" : "Unsupported")}; " +
            $"laterOverall={(ContractDocument.OverallSupported(text) ? "Supported" : "Unsupported")}",
            !string.Equals(numeric, text, StringComparison.Ordinal) &&
            ContractDocument.OverallSupported(numeric) &&
            ContractDocument.OverallSupported(text)));
    }

    private static void AddComparisonChecks(List<CheckOutcome> results)
    {
        IReadOnlyList<string> sameValue = JsonSubsetComparer.Compare("{\"a\":5}", "{\"a\":5.0}");
        IReadOnlyList<string> changedValue = JsonSubsetComparer.Compare("{\"a\":5}", "{\"a\":6}");
        IReadOnlyList<string> changedToken = JsonSubsetComparer.Compare("{\"a\":5}", "{\"a\":\"5\"}");
        IReadOnlyList<string> largeScale = JsonSubsetComparer.Compare("{\"a\":1E+400}", "{\"a\":1E+401}");
        IReadOnlyList<string> smallScale = JsonSubsetComparer.Compare("{\"a\":1E-400}", "{\"a\":0}");

        results.Add(Check.Assert(
            "C01.document-comparison.numbers-by-value",
            "Document comparison",
            "a number carries the same value in a different serialized form",
            "differences=0",
            $"differences={sameValue.Count}",
            $"5 versus 5.0: [{string.Join(", ", sameValue)}]",
            sameValue.Count == 0));

        results.Add(Check.Assert(
            "C01.document-comparison.number-and-token-changes",
            "Document comparison",
            "a number value changes, and a number becomes a string token",
            "differences=1 for each",
            $"valueChange={changedValue.Count}; tokenChange={changedToken.Count}",
            $"5 versus 6: [{string.Join(", ", changedValue)}]; 5 versus \"5\": [{string.Join(", ", changedToken)}]",
            changedValue.Count == 1 && changedToken.Count == 1));

        results.Add(Check.Assert(
            "C01.document-comparison.non-finite-numbers",
            "Document comparison",
            "a number is outside the range both decimal and finite double parsing can represent",
            "differences=1 for each",
            $"aboveDoubleRange={largeScale.Count}; belowDecimalRange={smallScale.Count}",
            $"1E+400 versus 1E+401: [{string.Join(", ", largeScale)}]; 1E-400 versus 0: [{string.Join(", ", smallScale)}]",
            largeScale.Count == 1 && smallScale.Count == 1));

    }
}
