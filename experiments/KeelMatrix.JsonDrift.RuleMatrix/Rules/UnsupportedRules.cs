using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R14 opaque converter handling and the rule that unsupported metadata never produces a green result.
/// </summary>
internal static class UnsupportedRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions withConverter = JsonContractOptions.Reflection(new MoneyConverter());

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
            "a custom converter is registered on the serializer options",
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
            roundTrip.Lossless && reason is not null));

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
}
