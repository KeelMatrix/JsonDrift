using System.Runtime.Serialization;
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

// Declarations outside the JsonAttribute base hierarchy and constructor selection.
internal sealed class RedundantJsonConstructorWithoutAttribute
{
    public RedundantJsonConstructorWithoutAttribute(int quantity) => Quantity = quantity;

    public int Quantity { get; }
}

internal sealed class RedundantJsonConstructorWithAttribute
{
    [JsonConstructor]
    public RedundantJsonConstructorWithAttribute(int quantity) => Quantity = quantity;

    public int Quantity { get; }
}

internal sealed class ConstructorBindingWithoutAttribute
{
    public ConstructorBindingWithoutAttribute() => Quantity = 1;

    public ConstructorBindingWithoutAttribute(int quantity) => Quantity = quantity;

    public int Quantity { get; }
}

internal sealed class ConstructorBindingWithAttribute
{
    public ConstructorBindingWithAttribute() => Quantity = 1;

    [JsonConstructor]
    public ConstructorBindingWithAttribute(int quantity) => Quantity = quantity;

    public int Quantity { get; }
}

internal sealed class IncludeAttributeHolder
{
    [JsonInclude]
    private int Secret { get; set; }

    public int Public { get; set; }
}

internal sealed class ObjectCreationHandlingAttributeHolder
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<int> Values { get; } = new();
}

internal sealed class PropertyOrderAttributeHolder
{
    [JsonPropertyOrder(1)]
    public int Quantity { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class UnmappedMemberHandlingAttributeHolder
{
    public int Quantity { get; set; }
}

internal enum StringEnumMemberNameValue
{
    [JsonStringEnumMemberName("created-order")]
    Created,
}

internal sealed class StringEnumMemberNameAttributeHolder
{
    public StringEnumMemberNameValue State { get; set; }
}

internal sealed class RuntimeSerializationBaselineHolder
{
    public int Quantity { get; set; }

    public int Omitted { get; set; }
}

[DataContract]
internal sealed class RuntimeSerializationAttributeHolder
{
    [DataMember(Name = "quantity")]
    public int Quantity { get; set; }

    [IgnoreDataMember]
    public int Omitted { get; set; }
}

[Serializable]
internal sealed class SerializableAttributeHolder
{
    public int Quantity { get; set; }
}
