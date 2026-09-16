using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R13 source-generated context metadata compared with reflection metadata.
/// </summary>
internal static class GenerationRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions omitsNulls = JsonContractOptions.OmitsNullMembers();

        TelemetryContext context = TelemetryContext.Default;

        // The two metadata sources are compared under option sets whose recorded values are equivalent, so the
        // verdict rests on the metadata source and not on an option difference the context declares.
        JsonTypeInfo fromReflectionContract = omitsNulls.GetTypeInfo(typeof(TelemetryBatch));
        string fromReflection = ContractCanonicalizer.Canonicalize(fromReflectionContract);
        string fromGeneration = ContractCanonicalizer.Canonicalize(context.TelemetryBatch);
        bool optionsEquivalent = OptionEquivalent(fromReflectionContract, context.TelemetryBatch);
        bool identical = string.Equals(fromReflection, fromGeneration, StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R13.source-generation.metadata-parity",
            "Source-generated metadata",
            "the same contract is described from reflection metadata and from a source-generated context under option sets whose recorded values are equivalent",
            "metadata=Equivalent, options=Equivalent",
            identical && optionsEquivalent
                ? "metadata=Equivalent, options=Equivalent"
                : $"metadata={(identical ? "Equivalent" : "Different")}, options={(optionsEquivalent ? "Equivalent" : "Different")}",
            $"reflection sha256={Sha256(fromReflection)}; source-generated sha256={Sha256(fromGeneration)}; identical={identical}; " +
            $"reflectionOptions=[{DescribeOptions(fromReflectionContract)}]; sourceGeneratedOptions=[{DescribeOptions(context.TelemetryBatch)}]",
            identical && optionsEquivalent));

        ReadOutcome generationOptions = WireProbe.Across(
            new TelemetryEvent { Name = "login", Detail = null, Count = 1 },
            reflection.GetTypeInfo(typeof(TelemetryEvent)),
            context.TelemetryEvent);

        results.AddRange(Check.Classify(
            "R13.source-generation.context-options",
            "Source-generated metadata",
            "the source-generated context declares a different default ignore condition",
            generationOptions,
            readerBackwardCompatible: false,
            WireProbe.Across(
                new TelemetryEvent { Name = "login", Detail = null, Count = 1 },
                context.TelemetryEvent,
                reflection.GetTypeInfo(typeof(TelemetryEvent))),
            writerForwardCompatible: true));

        var omittedTracking = new ShipmentV1 { Id = 1, Tracking = null };

        ReadOutcome reflectionRequiredness = WireProbe.Across(
            omittedTracking,
            omitsNulls.GetTypeInfo(typeof(ShipmentV1)),
            reflection.GetTypeInfo(typeof(ShipmentRequired)));

        ReadOutcome generatedRequiredness = WireProbe.Across(
            omittedTracking,
            context.ShipmentV1,
            context.ShipmentRequired);

        results.Add(Check.Assert(
            "R13.source-generation.classification-parity",
            "Source-generated metadata",
            "a required member is added and the document is read with both metadata sources",
            "ReaderBackward=Incompatible under both metadata sources",
            $"reflection={Classification(reflectionRequiredness)}, source-generated={Classification(generatedRequiredness)}",
            $"reflection: {Check.Describe(reflectionRequiredness)} | source-generated: {Check.Describe(generatedRequiredness)}",
            reflectionRequiredness.Lossless == generatedRequiredness.Lossless && !reflectionRequiredness.Lossless));

        bool unregisteredReported = IsUnregisteredTypeReported(context);

        results.Add(Check.Assert(
            "R13.source-generation.unregistered-type",
            "Source-generated metadata",
            "a type that the context does not register is requested from the context options",
            "metadata=Unavailable",
            unregisteredReported ? "metadata=Unavailable" : "metadata=Resolved",
            DescribeUnregisteredType(context),
            unregisteredReported));

        results.Add(Check.Compatible(
            "R13.source-generation.control",
            "Source-generated metadata",
            "the contract is unchanged",
            WireProbe.Across(
                new TelemetryEvent { Name = "login", Count = 1 },
                context.TelemetryEvent,
                context.TelemetryEvent)));

        return results;
    }

    private static string DescribeUnregisteredType(TelemetryContext context)
    {
        try
        {
            JsonSerializer.Serialize(new AccountV1 { Id = 1 }, context.Options);
            return "no error reported for the unregistered type";
        }
        catch (NotSupportedException exception)
        {
            return WireProbe.Summarize(exception);
        }
    }

    private static bool IsUnregisteredTypeReported(TelemetryContext context)
    {
        try
        {
            JsonSerializer.Serialize(new AccountV1 { Id = 1 }, context.Options);
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..16];

    /// <summary>
    /// Whether two contracts were recorded under option sets with the same recorded values, so a document
    /// comparison between their metadata sources is a comparison of the metadata and not of the options.
    /// </summary>
    private static bool OptionEquivalent(JsonTypeInfo earlier, JsonTypeInfo later) =>
        SerializerOptionFacts.Read(earlier.Options).Values
            .SequenceEqual(SerializerOptionFacts.Read(later.Options).Values);

    private static string DescribeOptions(JsonTypeInfo contract) =>
        string.Join(
            ", ",
            SerializerOptionFacts.Read(contract.Options).Values.Select(static value => value.Display));

    private static string Classification(ReadOutcome outcome) =>
        outcome.Lossless ? "ReaderBackward=Compatible" : "ReaderBackward=Incompatible";
}
