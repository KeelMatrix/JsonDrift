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
        JsonSerializerOptions omitsNulls = JsonContractOptions.Reflection();
        omitsNulls.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        TelemetryContext context = TelemetryContext.Default;

        string fromReflection = ContractCanonicalizer.Canonicalize(reflection.GetTypeInfo(typeof(TelemetryBatch)));
        string fromGeneration = ContractCanonicalizer.Canonicalize(context.TelemetryBatch);

        results.Add(Check.Assert(
            "R13.source-generation.metadata-parity",
            "Source-generated metadata",
            "the same contract is described from reflection metadata and from a source-generated context",
            "metadata=Equivalent",
            string.Equals(fromReflection, fromGeneration, StringComparison.Ordinal) ? "metadata=Equivalent" : "metadata=Different",
            $"reflection sha256={Sha256(fromReflection)}; source-generated sha256={Sha256(fromGeneration)}; identical={string.Equals(fromReflection, fromGeneration, StringComparison.Ordinal)}",
            string.Equals(fromReflection, fromGeneration, StringComparison.Ordinal)));

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

    private static string Classification(ReadOutcome outcome) =>
        outcome.Lossless ? "ReaderBackward=Compatible" : "ReaderBackward=Incompatible";
}
