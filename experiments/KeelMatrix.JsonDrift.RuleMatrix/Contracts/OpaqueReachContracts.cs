using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R14/R15 - opaque converter metadata reachable through an element, key, or value type, and through
// nested members below the first level.

[JsonConverter(typeof(ProbeMoneyConverter))]
internal sealed class ProbeMoney
{
    public decimal Amount { get; set; }
}

internal sealed class ProbeMoneyConverter : JsonConverter<ProbeMoney>
{
    public override ProbeMoney Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Amount = reader.GetDecimal() };

    public override void Write(Utf8JsonWriter writer, ProbeMoney value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Amount);
}

/// <summary>
/// An unrecognized converter for a bare value type, registered on the serializer options.
/// </summary>
internal sealed class OpaqueDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

internal sealed class ProbeElementList
{
    public List<ProbeMoney> Funds { get; set; } = new();
}

internal sealed class ProbeElementArray
{
    public ProbeMoney[] Funds { get; set; } = Array.Empty<ProbeMoney>();
}

internal sealed class ProbeNestedElementList
{
    public List<List<ProbeMoney>> Funds { get; set; } = new();
}

internal sealed class ProbeDeepElementList
{
    public List<List<List<ProbeMoney>>> Funds { get; set; } = new();
}

internal sealed class ProbeValueDictionary
{
    public Dictionary<string, ProbeMoney> Amounts { get; set; } = new();
}

internal sealed class ProbeNestedCombination
{
    public Dictionary<string, List<ProbeMoney>> Buckets { get; set; } = new();
}

internal sealed class ProbeAmountLedger
{
    public Dictionary<string, decimal> Amounts { get; set; } = new();
}

internal sealed class ProbeDepth1
{
    public ProbeMoney Money { get; set; } = new();
}

/// <summary>
/// A member whose declared type carries an unrecognized converter attribute, reached through the object
/// member path rather than through a member-level declaration.
/// </summary>
internal sealed class ProbeTypeAttributeHolder
{
    public ProbeMoney Money { get; set; } = new();
}

internal sealed class ProbeDepth2
{
    public ProbeDepth1 Next { get; set; } = new();
}

internal sealed class ProbeDepth3
{
    public ProbeDepth2 Next { get; set; } = new();
}

/// <summary>
/// A member nested deeper than the classification traversal budget. Exhausting the budget has to be
/// reported as unsupported rather than assumed classifiable.
/// </summary>
internal sealed class ProbeDepthLimitContract
{
    public List<List<List<List<List<List<List<List<List<int>>>>>>>>> Levels { get; set; } = new();
}

/// <summary>
/// A contract with one classifiable complex member and one member whose declared converter is opaque.
/// </summary>
internal sealed class MixedOpaqueContract
{
    public Money Amount { get; set; } = new();

    [JsonConverter(typeof(TemperatureConverter))]
    public Temperature Reading { get; set; } = new();
}

internal sealed class ShadowConverterHolder
{
    [JsonConverter(typeof(System.Text.Json.ProbeShadow.ShadowProbeMoneyConverter))]
    public System.Text.Json.ProbeShadow.ShadowProbeMoney Value { get; set; } = new();
}

/// <summary>
/// A dictionary key type whose declared converter produces a single opaque token, so a contract with it
/// cannot be classified even though every JSON object key is written as a string.
/// </summary>
[JsonConverter(typeof(ProbeKeyConverter))]
internal sealed class ProbeKey
{
    public int Value { get; set; }
}

internal sealed class ProbeKeyConverter : JsonConverter<ProbeKey>
{
    public override ProbeKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Value = reader.GetInt32() };

    public override void Write(Utf8JsonWriter writer, ProbeKey value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

internal sealed class ProbeKeyDictionary
{
    public Dictionary<ProbeKey, int> Totals { get; set; } = new();
}

/// <summary>
/// A constructor parameter type whose declared converter is opaque, reached through the binding metadata
/// rather than through a writable member.
/// </summary>
internal sealed class ConstructorOpaqueHolder
{
    public ConstructorOpaqueHolder(ProbeMoney amount)
    {
        Amount = amount;
    }

    public ProbeMoney Amount { get; }
}

/// <summary>
/// An extension-data member whose captured value type is converted by an unrecognized converter registered
/// on the serializer options, so what the contract captures cannot be classified.
/// </summary>
internal sealed class ExtensionDataOpaqueHolder
{
    public int Id { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class OpaqueJsonElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonDocument.ParseValue(ref reader).RootElement.Clone();

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
        value.WriteTo(writer);
}

/// <summary>
/// A contract whose member converter is supplied by the metadata resolver, so no converter attribute
/// declares it.
/// </summary>
internal sealed class ResolverInjectedHolder
{
    public decimal Amount { get; set; }
}

/// <summary>
/// A polymorphic base whose only registered derived type carries a member with an opaque converter. The
/// converter is reachable only through the derived-type metadata.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyLeaf), "leaf")]
internal abstract class PolyRoot
{
}

internal sealed class PolyLeaf : PolyRoot
{
    public ProbeMoney Amount { get; set; } = new();
}

/// <summary>
/// The same shape with the opaque converter registered on the serializer options instead of on the member
/// type, so only the walk of the derived type's members can find it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyOptionsLeaf), "leaf")]
internal abstract class PolyOptionsRoot
{
}

internal sealed class PolyOptionsLeaf : PolyOptionsRoot
{
    public decimal Amount { get; set; }
}

/// <summary>
/// A polymorphic member nested two levels below the root, with the opaque converter two derivation levels
/// below the first polymorphic base.
/// </summary>
internal sealed class ProbePolyNested
{
    public List<PolyNode> Nodes { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyNodeLeaf), "node")]
internal abstract class PolyNode
{
}

internal sealed class PolyNodeLeaf : PolyNode
{
    public List<PolyNestedNode> Nested { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyNestedLeaf), "nested")]
internal abstract class PolyNestedNode
{
}

internal sealed class PolyNestedLeaf : PolyNestedNode
{
    public ProbeMoney Amount { get; set; } = new();
}

/// <summary>
/// Two registrations of the same polymorphic base whose only difference is a member's token kind inside the
/// registered derived type: the discriminator, the type name, the member name, and every other member are
/// identical.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ShiftLeafNumeric), "shift")]
internal abstract class ShiftRootNumeric
{
}

internal sealed class ShiftLeafNumeric : ShiftRootNumeric
{
    public string Label { get; set; } = string.Empty;

    public int Amount { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ShiftLeafText), "shift")]
internal abstract class ShiftRootText
{
}

internal sealed class ShiftLeafText : ShiftRootText
{
    public string Label { get; set; } = string.Empty;

    public string Amount { get; set; } = string.Empty;
}
