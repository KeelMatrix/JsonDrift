namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R06 - token kind
internal sealed class QuantityInt
{
    public int Quantity { get; set; }
}

internal sealed class QuantityLong
{
    public long Quantity { get; set; }
}

internal sealed class QuantityShort
{
    public short Quantity { get; set; }
}

internal sealed class QuantityUnsigned
{
    public uint Quantity { get; set; }
}

internal sealed class QuantityDecimal
{
    public decimal Quantity { get; set; }
}

internal sealed class QuantityString
{
    public string Quantity { get; set; } = string.Empty;
}

// R07 - collection, array, and dictionary shape
internal sealed class LinesList
{
    public List<int> Lines { get; set; } = new();
}

internal sealed class LinesArray
{
    public int[] Lines { get; set; } = Array.Empty<int>();
}

internal sealed class LinesDictionary
{
    public Dictionary<string, int> Lines { get; set; } = new();
}

internal sealed class LineScalar
{
    public int Lines { get; set; }
}

internal sealed class TotalsStringKey
{
    public Dictionary<string, int> Totals { get; set; } = new();
}

internal sealed class TotalsIntKey
{
    public Dictionary<int, int> Totals { get; set; } = new();
}

internal sealed class NestedEnvelopeV1
{
    public NestedPayloadV1 Child { get; set; } = new();
}

internal sealed class NestedPayloadV1
{
    public int Value { get; set; }
}

internal sealed class NestedEnvelopeV2
{
    public NestedPayloadV2 Child { get; set; } = new();
}

internal sealed class NestedPayloadV2
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class NestedCollectionEnvelopeV1
{
    public List<NestedCollectionItemV1> Items { get; set; } = new();
}

internal sealed class NestedCollectionItemV1
{
    public int Value { get; set; }
}

internal sealed class NestedCollectionEnvelopeV2
{
    public List<NestedCollectionItemV2> Items { get; set; } = new();
}

internal sealed class NestedCollectionItemV2
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class NestedDictionaryEnvelopeV1
{
    public Dictionary<string, NestedDictionaryItemV1> Values { get; set; } = new();
}

internal sealed class NestedDictionaryItemV1
{
    public int Value { get; set; }
}

internal sealed class NestedDictionaryEnvelopeV2
{
    public Dictionary<string, NestedDictionaryItemV2> Values { get; set; } = new();
}

internal sealed class NestedDictionaryItemV2
{
    public string Value { get; set; } = string.Empty;
}
