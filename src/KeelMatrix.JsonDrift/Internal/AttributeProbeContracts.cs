using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.Internal;

// These private declarations are measurement fixtures for the allowlist owned by the shipping assembly.
// They are never exposed as an application model; the concrete root keeps attribute measurement independent
// from abstract/interface reader-materialization classification.
internal sealed class AllowlistedAttributeProbe
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProbeAttributeEnum State { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }

    [JsonIgnore]
    public string? Ignored { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ExplicitlyIncluded { get; set; }

    [JsonNumberHandling(JsonNumberHandling.Strict)]
    public int Count { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("accountId")]
    public string? AlternateAccountId { get; set; }

    [JsonRequired]
    public string RequiredValue { get; set; } = string.Empty;

}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AttributeProbeKindDog), "dog")]
internal abstract class AttributeProbePolymorphicKindRoot
{
}

internal sealed class AttributeProbeKindDog : AttributeProbePolymorphicKindRoot
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AttributeProbePolyDog), "dog")]
[JsonDerivedType(typeof(AttributeProbePolyCat), "cat")]
[JsonDerivedType(typeof(AttributeProbePolyCanine), "canine")]
[JsonDerivedType(typeof(AttributeProbePolyEnum), "enum")]
[JsonDerivedType(typeof(AttributeProbePolyShift), "shift")]
internal abstract class AttributeProbePolymorphicRoot
{
    public string? Name { get; set; }
}

internal sealed class AttributeProbePolyDog : AttributeProbePolymorphicRoot
{
}

internal sealed class AttributeProbePolyCat : AttributeProbePolymorphicRoot
{
}

internal sealed class AttributeProbePolyCanine : AttributeProbePolymorphicRoot
{
}

internal sealed class AttributeProbePolyEnum : AttributeProbePolymorphicRoot
{
}

internal sealed class AttributeProbePolyShift : AttributeProbePolymorphicRoot
{
}

internal enum ProbeAttributeEnum
{
    First,
    Second,
}

internal enum InventoryAttributeEnum
{
    [JsonStringEnumMemberName("created-order")]
    First,
    Second,
}

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
internal sealed class InventoryAttributeProbe
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public int Number { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Defaulted { get; set; }

    [JsonInclude]
    private int Included { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<int> Values { get; } = new();

    [JsonPropertyOrder(1)]
    public string? Ordered { get; set; }

    public InventoryAttributeEnum Named { get; set; }

    [JsonConstructor]
    public InventoryAttributeProbe(int number = 0) => Number = number;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class UnmappedAttributeProbe
{
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AllowlistedAttributeProbe))]
[JsonSerializable(typeof(InventoryAttributeProbe))]
internal partial class AttributeProbeContext : JsonSerializerContext
{
}
