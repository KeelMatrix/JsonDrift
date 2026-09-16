using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R01 property addition, R02 property removal, R03 serialized property-name changes.
/// </summary>
internal static class PropertyRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();

        AddPropertyAddition(results, reflection);
        AddPropertyRemoval(results, reflection);
        AddSerializedNameChange(results, reflection);

        return results;
    }

    private static void AddPropertyAddition(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        JsonTypeInfo v1 = reflection.GetTypeInfo(typeof(TicketV1));
        JsonTypeInfo v2 = reflection.GetTypeInfo(typeof(TicketV2));
        JsonTypeInfo v2Ignored = reflection.GetTypeInfo(typeof(TicketV2Ignored));

        var earlier = new TicketV1 { Id = 1 };
        var later = new TicketV2 { Id = 1, Note = "urgent" };
        var laterIgnored = new TicketV2Ignored { Id = 1, Note = "urgent" };

        results.AddRange(Check.Classify(
            "R01.property-add.optional",
            "Property addition",
            "an optional member is added to an existing contract",
            WireProbe.Across(earlier, v1, v2),
            readerBackwardCompatible: true,
            WireProbe.Across(later, v2, v1),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R01.property-add.ignored",
            "Property addition",
            "the added member is excluded from the wire contract",
            WireProbe.Across(earlier, v1, v2Ignored),
            readerBackwardCompatible: true,
            WireProbe.Across(laterIgnored, v2Ignored, v1),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R01.property-add.optional.control",
            "Property addition",
            "the contract is unchanged",
            WireProbe.Across(laterIgnored, v2Ignored, v2Ignored)));
    }

    private static void AddPropertyRemoval(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        JsonTypeInfo v1 = reflection.GetTypeInfo(typeof(CustomerV1));
        JsonTypeInfo v2 = reflection.GetTypeInfo(typeof(CustomerV2));

        var earlier = new CustomerV1 { Name = "Ada", Email = "ada@example.com" };
        var later = new CustomerV2 { Name = "Ada" };

        results.AddRange(Check.Classify(
            "R02.property-removal",
            "Property removal",
            "a serialized member is removed from the contract",
            WireProbe.Across(earlier, v1, v2),
            readerBackwardCompatible: false,
            WireProbe.Across(later, v2, v1),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R02.property-removal.control",
            "Property removal",
            "the contract is unchanged",
            WireProbe.Across(later, v2, v2)));
    }

    private static void AddSerializedNameChange(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        JsonTypeInfo accountV1 = reflection.GetTypeInfo(typeof(AccountV1));
        JsonTypeInfo accountV2 = reflection.GetTypeInfo(typeof(AccountV2));
        JsonTypeInfo accountV2WithExtensionData = reflection.GetTypeInfo(typeof(AccountV2WithExtensionData));

        var earlier = new AccountV1 { Id = 7 };
        var later = new AccountV2 { Id = 7 };

        results.AddRange(Check.Classify(
            "R03.serialized-name.json-property-name",
            "Serialized property-name change",
            "the serialized name of a member is renamed",
            WireProbe.Across(earlier, accountV1, accountV2),
            readerBackwardCompatible: false,
            WireProbe.Across(later, accountV2, accountV1),
            writerForwardCompatible: false));

        JsonSerializerOptions camelCase = JsonContractOptions.Reflection();
        camelCase.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        JsonSerializerOptions snakeCase = JsonContractOptions.Reflection();
        snakeCase.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;

        var invoice = new InvoiceAmounts { AmountDue = 12.5m };

        results.AddRange(Check.Classify(
            "R03.serialized-name.naming-policy",
            "Serialized property-name change",
            "the naming policy applied to the contract changes",
            WireProbe.Across(invoice, camelCase.GetTypeInfo(typeof(InvoiceAmounts)), snakeCase.GetTypeInfo(typeof(InvoiceAmounts))),
            readerBackwardCompatible: false,
            WireProbe.Across(invoice, snakeCase.GetTypeInfo(typeof(InvoiceAmounts)), camelCase.GetTypeInfo(typeof(InvoiceAmounts))),
            writerForwardCompatible: false));

        results.Add(Check.Compatible(
            "R03.serialized-name.naming-policy.control",
            "Serialized property-name change",
            "the naming policy is unchanged",
            WireProbe.Across(invoice, camelCase.GetTypeInfo(typeof(InvoiceAmounts)), camelCase.GetTypeInfo(typeof(InvoiceAmounts)))));

        results.Add(Check.Compatible(
            "R03.serialized-name.mitigated-by-extension-data",
            "Serialized property-name change",
            "a rename is combined with capturing unrecognized members",
            WireProbe.Across(earlier, accountV1, accountV2WithExtensionData)));
    }
}
