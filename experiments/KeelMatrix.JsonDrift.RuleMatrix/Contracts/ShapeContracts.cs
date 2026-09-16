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
