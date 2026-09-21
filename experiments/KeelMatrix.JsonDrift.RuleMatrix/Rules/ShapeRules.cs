using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift;
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
        AddPublicComparisonRegressions(results, reflection);

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

    private static void AddPublicComparisonRegressions(List<CheckOutcome> results, JsonSerializerOptions reflection)
    {
        JsonTypeInfo integer = reflection.GetTypeInfo(typeof(QuantityInt));
        JsonTypeInfo text = reflection.GetTypeInfo(typeof(QuantityString));
        JsonDriftReport tokenReport = JsonDrift.Compare(
            text,
            JsonDrift.Extract(integer),
            JsonCompatibility.ReaderBackward);

        JsonDriftReport scalarReport = JsonDrift.Compare(
            reflection.GetTypeInfo(typeof(Guid)),
            JsonDrift.Extract(reflection.GetTypeInfo(typeof(DateTime))),
            JsonCompatibility.ReaderBackward);

        bool tokenAssertionFailed = AssertNotCompatible(tokenReport);
        bool scalarAssertionFailed = AssertNotCompatible(scalarReport);
        results.Add(Check.Assert(
            "R06.token-kind.public-comparison",
            "Shipping comparison semantics",
            "the public comparison and assertion reject a root numeric-to-string token change and fail closed for an unclassified string scalar transition",
            "root number-to-string=Incompatible, root scalar transition=Unsupported, assertions=throw",
            $"root number-to-string={tokenReport.Outcome}; root scalar transition={scalarReport.Outcome}; assertions=throw({tokenAssertionFailed && scalarAssertionFailed})",
            $"tokenChanges={tokenReport.Changes.Count}; scalarRule={string.Join(',', scalarReport.Changes.Select(static change => change.RuleId))}",
            tokenReport.Outcome == JsonDriftClassification.Incompatible &&
            scalarReport.Outcome == JsonDriftClassification.Unsupported &&
            tokenAssertionFailed &&
            scalarAssertionFailed));

        JsonTypeInfo shortType = reflection.GetTypeInfo(typeof(QuantityShort));
        JsonTypeInfo unsignedType = reflection.GetTypeInfo(typeof(QuantityUnsigned));
        JsonTypeInfo decimalType = reflection.GetTypeInfo(typeof(QuantityDecimal));
        JsonDriftReport intToShort = JsonDrift.Compare(shortType, JsonDrift.Extract(integer), JsonCompatibility.ReaderBackward);
        JsonDriftReport intToUnsigned = JsonDrift.Compare(unsignedType, JsonDrift.Extract(integer), JsonCompatibility.ReaderBackward);
        JsonDriftReport decimalToInt = JsonDrift.Compare(integer, JsonDrift.Extract(decimalType), JsonCompatibility.ReaderBackward);

        ReadOutcome intToShortWire = WireProbe.Across(new QuantityInt { Quantity = 40_000 }, integer, shortType);
        ReadOutcome intToUnsignedWire = WireProbe.Across(new QuantityInt { Quantity = -1 }, integer, unsignedType);
        ReadOutcome decimalToIntWire = WireProbe.Across(new QuantityDecimal { Quantity = 1.5m }, decimalType, integer);
        bool numericAssertionsFailed = AssertNotCompatible(intToShort) &&
            AssertNotCompatible(intToUnsigned) &&
            AssertNotCompatible(decimalToInt);
        results.Add(Check.Assert(
            "R06.token-kind.numeric-boundaries.public-comparison",
            "Shipping comparison semantics",
            "boundary-valued serializer witnesses agree with public comparison for int-to-short, int-to-uint, and decimal-to-int",
            "all three wire probes reject, all three reports=Incompatible, assertions=throw",
            $"wire=({Check.Describe(intToShortWire)} | {Check.Describe(intToUnsignedWire)} | {Check.Describe(decimalToIntWire)}); reports=({intToShort.Outcome}, {intToUnsigned.Outcome}, {decimalToInt.Outcome}); assertions=throw({numericAssertionsFailed})",
            "the reported rule is numeric-narrowing for each counterexample",
            intToShortWire.Fault is null && !intToShortWire.Lossless &&
            intToUnsignedWire.Fault is null && !intToUnsignedWire.Lossless &&
            decimalToIntWire.Fault is null && !decimalToIntWire.Lossless &&
            intToShort.Outcome == JsonDriftClassification.Incompatible &&
            intToUnsigned.Outcome == JsonDriftClassification.Incompatible &&
            decimalToInt.Outcome == JsonDriftClassification.Incompatible &&
            numericAssertionsFailed));

        JsonDriftReport nested = JsonDrift.Compare(
            reflection.GetTypeInfo(typeof(NestedEnvelopeV2)),
            JsonDrift.Extract(reflection.GetTypeInfo(typeof(NestedEnvelopeV1))),
            JsonCompatibility.ReaderBackward);
        JsonDriftReport collection = JsonDrift.Compare(
            reflection.GetTypeInfo(typeof(NestedCollectionEnvelopeV2)),
            JsonDrift.Extract(reflection.GetTypeInfo(typeof(NestedCollectionEnvelopeV1))),
            JsonCompatibility.ReaderBackward);
        JsonDriftReport dictionary = JsonDrift.Compare(
            reflection.GetTypeInfo(typeof(NestedDictionaryEnvelopeV2)),
            JsonDrift.Extract(reflection.GetTypeInfo(typeof(NestedDictionaryEnvelopeV1))),
            JsonCompatibility.ReaderBackward);
        bool nestedAssertionsFailed = AssertNotCompatible(nested) &&
            AssertNotCompatible(collection) &&
            AssertNotCompatible(dictionary);

        results.Add(Check.Assert(
            "R07.shape.nested-contract.public-comparison",
            "Shipping comparison semantics",
            "nested object, collection element, and dictionary value contract changes are compared through the public API",
            "all three reports=Incompatible, assertions=throw",
            $"reports=({nested.Outcome}, {collection.Outcome}, {dictionary.Outcome}); assertions=throw({nestedAssertionsFailed})",
            $"paths=({string.Join(',', nested.Changes.Select(static change => change.Path))}; {string.Join(',', collection.Changes.Select(static change => change.Path))}; {string.Join(',', dictionary.Changes.Select(static change => change.Path))})",
            nested.Outcome == JsonDriftClassification.Incompatible &&
            collection.Outcome == JsonDriftClassification.Incompatible &&
            dictionary.Outcome == JsonDriftClassification.Incompatible &&
            nestedAssertionsFailed));
    }

    private static bool AssertNotCompatible(JsonDriftReport report)
    {
        try
        {
            report.AssertCompatible();
            return false;
        }
        catch (JsonDriftCompatibilityException)
        {
            return true;
        }
    }
}
