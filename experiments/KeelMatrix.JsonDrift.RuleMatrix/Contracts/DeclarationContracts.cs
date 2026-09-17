using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// F9 - declared number-handling attributes. Strict is the measured accepted value; WriteAsString is the
// adversarial value because it changes the wire token and the earlier contract rejects the new document.
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed class StrictNumberType
{
    public int Quantity { get; set; }
}

internal sealed class StrictNumberMember
{
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int Quantity { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
internal sealed class WriteAsStringNumberType
{
    public int Quantity { get; set; }
}

internal sealed class WriteAsStringNumberMember
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public int Quantity { get; set; }
}

// F9b - Never is the measured accepted declaration; WhenWritingDefault is the adversarial value.
internal sealed class NeverIgnoredMember
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int Quantity { get; set; }
}

internal sealed class DefaultIgnoredMember
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Quantity { get; set; }
}
