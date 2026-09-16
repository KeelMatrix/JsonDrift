using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R05 nullability: changes in the nullable shape of a member, and whether the reader enforces it.
/// </summary>
internal static class NullabilityRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions enforcing = JsonContractOptions.Reflection();
        enforcing.RespectNullableAnnotations = true;

        JsonTypeInfo nullableReference = reflection.GetTypeInfo(typeof(ProfileV1Nullable));
        JsonTypeInfo nonNullableReference = reflection.GetTypeInfo(typeof(ProfileV2NonNullable));
        JsonTypeInfo nonNullableReferenceEnforcing = enforcing.GetTypeInfo(typeof(ProfileV2NonNullable));

        var nullableProfile = new ProfileV1Nullable { Name = null };
        var nonNullableProfile = new ProfileV2NonNullable { Name = "Ada" };

        results.Add(Check.Assert(
            "R05.nullability.metadata",
            "Nullability change",
            "a nullable reference member becomes non-nullable",
            "metadata=Trustworthy",
            "metadata=Trustworthy",
            DescribeNullability(nullableReference, nonNullableReference),
            IsNullable(nullableReference) && !IsNullable(nonNullableReference)));

        results.AddRange(Check.Classify(
            "R05.nullability.reference-nullable-removed",
            "Nullability change",
            "a nullable reference member becomes non-nullable, read with default options",
            WireProbe.Across(nullableProfile, nullableReference, nonNullableReference),
            readerBackwardCompatible: true,
            WireProbe.Across(nonNullableProfile, nonNullableReference, nullableReference),
            writerForwardCompatible: true));

        results.Add(Check.Incompatible(
            "R05.nullability.reference-nullable-removed.enforced",
            "Nullability change",
            "a nullable reference member becomes non-nullable, read with nullable annotations enforced",
            WireProbe.Across(nullableProfile, nullableReference, nonNullableReferenceEnforcing)));

        JsonTypeInfo nullableValue = reflection.GetTypeInfo(typeof(CounterV1Nullable));
        JsonTypeInfo nonNullableValue = reflection.GetTypeInfo(typeof(CounterV2NonNullable));
        JsonTypeInfo nonNullableValueV1 = reflection.GetTypeInfo(typeof(CounterV1NonNullable));
        JsonTypeInfo nullableValueV2 = reflection.GetTypeInfo(typeof(CounterV2Nullable));

        results.AddRange(Check.Classify(
            "R05.nullability.value-nullable-removed",
            "Nullability change",
            "a nullable value member becomes non-nullable",
            WireProbe.Across(new CounterV1Nullable { Quantity = null }, nullableValue, nonNullableValue),
            readerBackwardCompatible: false,
            WireProbe.Across(new CounterV2NonNullable { Quantity = 3 }, nonNullableValue, nullableValue),
            writerForwardCompatible: true));

        results.AddRange(Check.Classify(
            "R05.nullability.value-nullable-added",
            "Nullability change",
            "a non-nullable value member becomes nullable",
            WireProbe.Across(new CounterV1NonNullable { Quantity = 3 }, nonNullableValueV1, nullableValueV2),
            readerBackwardCompatible: true,
            WireProbe.Across(new CounterV2Nullable { Quantity = null }, nullableValueV2, nonNullableValueV1),
            writerForwardCompatible: false));

        results.Add(Check.Compatible(
            "R05.nullability.control",
            "Nullability change",
            "the contract is unchanged",
            WireProbe.Across(nullableProfile, nullableReference, nullableReference)));

        return results;
    }

    private static bool IsNullable(JsonTypeInfo contract) =>
        contract.Properties.Any(property => property.IsGetNullable || property.IsSetNullable);

    private static string DescribeNullability(JsonTypeInfo earlier, JsonTypeInfo later) =>
        $"earlier getNullable={IsNullable(earlier)} setNullable={IsNullable(earlier)}; later getNullable={IsNullable(later)} setNullable={IsNullable(later)}";
}
