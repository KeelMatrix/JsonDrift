using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R11 members the contract captures even though no property binds them.
/// </summary>
internal static class ExtensionDataRules
{
    private const string DocumentWithUnknownMember = "{\"Id\":1,\"legacy\":true}";

    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();

        JsonTypeInfo v1 = reflection.GetTypeInfo(typeof(EnvelopeV1));
        JsonTypeInfo v2WithoutExtensionData = reflection.GetTypeInfo(typeof(EnvelopeV2WithoutExtensionData));
        JsonTypeInfo withExtensionData = reflection.GetTypeInfo(typeof(EnvelopeWithExtensionData));

        var capturing = new EnvelopeWithExtensionData
        {
            Id = 1,
            Extra = new Dictionary<string, JsonElement>
            {
                ["legacy"] = JsonDocument.Parse("true").RootElement,
            },
        };

        results.AddRange(Check.Classify(
            "R11.extension-data.removed",
            "Captured members",
            "the contract stops capturing members without a matching property",
            WireProbe.Across(capturing, withExtensionData, v2WithoutExtensionData),
            readerBackwardCompatible: false,
            WireProbe.Across(new EnvelopeV2WithoutExtensionData { Id = 1 }, v2WithoutExtensionData, withExtensionData),
            writerForwardCompatible: true));

        results.AddRange(Check.Classify(
            "R11.extension-data.added",
            "Captured members",
            "the contract starts capturing members without a matching property",
            WireProbe.Across(new EnvelopeV1 { Id = 1 }, v1, withExtensionData),
            readerBackwardCompatible: true,
            WireProbe.Across(capturing, withExtensionData, v1),
            writerForwardCompatible: false));

        results.Add(Check.Incompatible(
            "R11.extension-data.absent.loses-unbound-member",
            "Captured members",
            "a document with an unbound member is read by a contract that captures nothing",
            WireProbe.Read(DocumentWithUnknownMember, v1)));

        results.Add(Check.Compatible(
            "R11.extension-data.present.preserves-unbound-member",
            "Captured members",
            "a document with an unbound member is read by a contract that captures members",
            WireProbe.Read(DocumentWithUnknownMember, withExtensionData)));

        results.Add(Check.Compatible(
            "R11.extension-data.control",
            "Captured members",
            "the contract is unchanged",
            WireProbe.Across(new EnvelopeV1 { Id = 1 }, v1, v1)));

        return results;
    }
}
