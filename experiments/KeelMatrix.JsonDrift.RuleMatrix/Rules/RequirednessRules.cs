using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R04 requiredness: whether a member must be present in the document.
/// </summary>
internal static class RequirednessRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions omitsNulls = JsonContractOptions.OmitsNullMembers();

        JsonTypeInfo optional = reflection.GetTypeInfo(typeof(ShipmentV1));
        JsonTypeInfo required = reflection.GetTypeInfo(typeof(ShipmentRequired));
        JsonTypeInfo requiredKeyword = reflection.GetTypeInfo(typeof(ShipmentRequiredKeyword));
        JsonTypeInfo optionalOmittingNulls = omitsNulls.GetTypeInfo(typeof(ShipmentV1));

        var optionalValue = new ShipmentV1 { Id = 1, Tracking = null };
        var requiredValue = new ShipmentRequired { Id = 1, Tracking = "TRK-1" };

        results.AddRange(Check.Classify(
            "R04.requiredness.optional-to-required",
            "Requiredness change",
            "an optional member becomes required",
            WireProbe.Across(optionalValue, optionalOmittingNulls, required),
            readerBackwardCompatible: false,
            WireProbe.Across(requiredValue, required, optional),
            writerForwardCompatible: true));

        results.AddRange(Check.Classify(
            "R04.requiredness.required-to-optional",
            "Requiredness change",
            "a required member becomes optional",
            WireProbe.Across(requiredValue, required, optional),
            readerBackwardCompatible: true,
            WireProbe.Across(optionalValue, optionalOmittingNulls, required),
            writerForwardCompatible: false));

        results.Add(Check.Incompatible(
            "R04.requiredness.required-keyword",
            "Requiredness change",
            "an optional member becomes required through the required modifier",
            WireProbe.Across(optionalValue, optionalOmittingNulls, requiredKeyword)));

        results.Add(Check.Compatible(
            "R04.requiredness.explicit-null",
            "Requiredness change",
            "a required member is present in the document as an explicit null",
            WireProbe.Read("{\"Id\":1,\"Tracking\":null}", required)));

        results.Add(Check.Compatible(
            "R04.requiredness.control",
            "Requiredness change",
            "the contract is unchanged",
            WireProbe.Across(optionalValue, optional, optional)));

        return results;
    }
}
