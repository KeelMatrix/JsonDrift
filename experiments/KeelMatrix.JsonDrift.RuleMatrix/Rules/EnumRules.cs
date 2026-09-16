using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R08 enum representation: numeric tokens, string tokens, and member identity.
/// </summary>
internal static class EnumRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions numeric = JsonContractOptions.Reflection();
        JsonSerializerOptions text = JsonContractOptions.Reflection(new JsonStringEnumConverter());
        JsonSerializerOptions textOnly = JsonContractOptions.Reflection(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));

        JsonTypeInfo numericState = numeric.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo renamedState = text.GetTypeInfo(typeof(StateHolderRenamed));
        JsonTypeInfo textState = text.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo textOnlyState = textOnly.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo extendedState = numeric.GetTypeInfo(typeof(StateHolderExtended));

        var numericValue = new StateHolderNumeric { State = OrderState.Shipped };
        var textValue = new StateHolderNumeric { State = OrderState.Shipped };

        results.AddRange(Check.Classify(
            "R08.enum.member-rename",
            "Enum representation change",
            "an enum member is renamed while the contract keeps string tokens",
            WireProbe.Across(textValue, textState, renamedState),
            readerBackwardCompatible: false,
            WireProbe.Across(new StateHolderRenamed { State = OrderStateRenamed.Dispatched }, renamedState, textState),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R08.enum.number-to-string",
            "Enum representation change",
            "an enum member is written as a JSON string instead of a number",
            WireProbe.Across(numericValue, numericState, textState),
            readerBackwardCompatible: false,
            WireProbe.Across(textValue, textState, numericState),
            writerForwardCompatible: false));

        results.Add(Check.Incompatible(
            "R08.enum.number-to-string.tokens-only",
            "Enum representation change",
            "string tokens are required and integer tokens are rejected",
            WireProbe.Across(numericValue, numericState, textOnlyState)));

        results.AddRange(Check.Classify(
            "R08.enum.member-insertion",
            "Enum representation change",
            "an enum member is inserted into a numeric representation",
            WireProbe.Across(numericValue, numericState, extendedState),
            readerBackwardCompatible: true,
            WireProbe.Across(new StateHolderExtended { State = OrderStateExtended.Packed }, extendedState, numericState),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R08.enum.control",
            "Enum representation change",
            "the contract is unchanged",
            WireProbe.Across(numericValue, numericState, numericState)));

        return results;
    }
}
