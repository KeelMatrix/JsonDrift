using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R12 constructor binding: parameters added, defaulted, or renamed.
/// </summary>
internal static class BindingRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions strictConstructors = JsonContractOptions.Reflection();
        strictConstructors.RespectRequiredConstructorParameters = true;

        JsonTypeInfo v1 = reflection.GetTypeInfo(typeof(ShipmentEventV1));
        JsonTypeInfo v2 = reflection.GetTypeInfo(typeof(ShipmentEventV2));
        JsonTypeInfo v2Defaulted = reflection.GetTypeInfo(typeof(ShipmentEventV2Defaulted));
        JsonTypeInfo v2Strict = strictConstructors.GetTypeInfo(typeof(ShipmentEventV2));

        var earlier = new ShipmentEventV1("S1");
        var later = new ShipmentEventV2("S1", "carrier");
        var laterDefaulted = new ShipmentEventV2Defaulted("S1");

        results.AddRange(Check.Classify(
            "R12.binding.constructor-parameter-added",
            "Constructor binding change",
            "a constructor parameter is added to a bound constructor",
            WireProbe.Across(earlier, v1, v2),
            readerBackwardCompatible: true,
            WireProbe.Across(later, v2, v1),
            writerForwardCompatible: false));

        results.Add(Check.Incompatible(
            "R12.binding.constructor-parameter-added.enforced",
            "Constructor binding change",
            "a constructor parameter is added while missing parameters are rejected",
            WireProbe.Across(earlier, v1, v2Strict)));

        results.AddRange(Check.Classify(
            "R12.binding.constructor-parameter-defaulted",
            "Constructor binding change",
            "an added constructor parameter has a default value",
            WireProbe.Across(earlier, v1, v2Defaulted),
            readerBackwardCompatible: true,
            WireProbe.Across(laterDefaulted, v2Defaulted, v1),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R12.binding.constructor-parameter-renamed",
            "Constructor binding change",
            "a constructor parameter that binds by name is renamed",
            WireProbe.Across(new QuoteV1(12.5m), reflection.GetTypeInfo(typeof(QuoteV1)), reflection.GetTypeInfo(typeof(QuoteV2))),
            readerBackwardCompatible: false,
            WireProbe.Across(new QuoteV2(12.5m), reflection.GetTypeInfo(typeof(QuoteV2)), reflection.GetTypeInfo(typeof(QuoteV1))),
            writerForwardCompatible: false));

        results.Add(Check.Compatible(
            "R12.binding.control",
            "Constructor binding change",
            "the contract is unchanged",
            WireProbe.Across(later, v2, v2)));

        return results;
    }
}
