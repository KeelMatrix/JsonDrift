using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R06 JSON token-kind changes, R07 collection, array, and dictionary shape changes.
/// </summary>
internal static class ShapeRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();

        AddTokenKind(results, reflection);
        AddCollectionShape(results, reflection);

        return results;
    }

    private static void AddTokenKind(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        JsonTypeInfo integer = reflection.GetTypeInfo(typeof(QuantityInt));
        JsonTypeInfo longerInteger = reflection.GetTypeInfo(typeof(QuantityLong));
        JsonTypeInfo text = reflection.GetTypeInfo(typeof(QuantityString));

        JsonSerializerOptions readsNumbersFromStrings = JsonContractOptions.Reflection();
        readsNumbersFromStrings.NumberHandling = JsonNumberHandling.AllowReadingFromString;

        var integerValue = new QuantityInt { Quantity = 5 };
        var textValue = new QuantityString { Quantity = "5" };

        results.AddRange(Check.Classify(
            "R06.token-kind.number-to-string",
            "Token-kind change",
            "a numeric member is written as a JSON string",
            WireProbe.Across(integerValue, integer, text),
            readerBackwardCompatible: false,
            WireProbe.Across(textValue, text, integer),
            writerForwardCompatible: false));

        results.Add(Check.Incompatible(
            "R06.token-kind.string-to-number.permissive",
            "Token-kind change",
            "a string member is read as a number with reading from strings allowed",
            WireProbe.Across(textValue, text, readsNumbersFromStrings.GetTypeInfo(typeof(QuantityInt)))));

        results.AddRange(Check.Classify(
            "R06.token-kind.numeric-widening",
            "Token-kind change",
            "an int member widens to long",
            WireProbe.Across(new QuantityInt { Quantity = int.MaxValue }, integer, longerInteger),
            readerBackwardCompatible: true,
            WireProbe.Across(new QuantityLong { Quantity = int.MinValue }, longerInteger, integer),
            writerForwardCompatible: true));

        results.AddRange(Check.Classify(
            "R06.token-kind.numeric-narrowing",
            "Token-kind change",
            "a long member narrows to int and the earlier document carries a value outside the int range",
            WireProbe.Across(new QuantityLong { Quantity = 3_000_000_000L }, longerInteger, integer),
            readerBackwardCompatible: false,
            WireProbe.Across(new QuantityInt { Quantity = 5 }, integer, longerInteger),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R06.token-kind.control",
            "Token-kind change",
            "the contract is unchanged",
            WireProbe.Across(integerValue, integer, integer)));
    }

    private static void AddCollectionShape(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        JsonTypeInfo list = reflection.GetTypeInfo(typeof(LinesList));
        JsonTypeInfo array = reflection.GetTypeInfo(typeof(LinesArray));
        JsonTypeInfo dictionary = reflection.GetTypeInfo(typeof(LinesDictionary));
        JsonTypeInfo scalar = reflection.GetTypeInfo(typeof(LineScalar));
        JsonTypeInfo stringKeyedTotals = reflection.GetTypeInfo(typeof(TotalsStringKey));
        JsonTypeInfo intKeyedTotals = reflection.GetTypeInfo(typeof(TotalsIntKey));

        var listed = new LinesList { Lines = { 1, 2 } };
        var arrayed = new LinesArray { Lines = new[] { 1, 2 } };
        var mapped = new LinesDictionary { Lines = { ["first"] = 1 } };

        results.AddRange(Check.Classify(
            "R07.shape.list-to-array",
            "Collection shape change",
            "a list member becomes an array member",
            WireProbe.Across(listed, list, array),
            readerBackwardCompatible: true,
            WireProbe.Across(arrayed, array, list),
            writerForwardCompatible: true));

        results.AddRange(Check.Classify(
            "R07.shape.list-to-dictionary",
            "Collection shape change",
            "an array member becomes an object member",
            WireProbe.Across(listed, list, dictionary),
            readerBackwardCompatible: false,
            WireProbe.Across(mapped, dictionary, list),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R07.shape.dictionary-to-scalar",
            "Collection shape change",
            "an object member becomes a scalar member",
            WireProbe.Across(mapped, dictionary, scalar),
            readerBackwardCompatible: false,
            WireProbe.Across(new LineScalar { Lines = 1 }, scalar, dictionary),
            writerForwardCompatible: false));

        results.Add(Check.Unsupported(
            "R07.shape.dictionary-key-type",
            "Collection shape change",
            "a dictionary key type changes while JSON object keys stay strings",
            DictionaryKeyCompatibility.DescribeUnsupported(typeof(string), typeof(int))));

        results.Add(Check.Compatible(
            "R07.shape.dictionary-key-type.representable",
            "Collection shape change",
            "an earlier string key is representable in the later integer key type",
            WireProbe.Across(new TotalsStringKey { Totals = { ["1"] = 2 } }, stringKeyedTotals, intKeyedTotals)));

        results.Add(Check.Incompatible(
            "R07.shape.dictionary-key-type.unrepresentable",
            "Collection shape change",
            "an earlier string key is not representable in the later integer key type",
            WireProbe.Across(new TotalsStringKey { Totals = { ["abc"] = 2 } }, stringKeyedTotals, intKeyedTotals)));

        results.Add(Check.Compatible(
            "R07.shape.control",
            "Collection shape change",
            "the contract is unchanged",
            WireProbe.Across(listed, list, list)));
    }
}
