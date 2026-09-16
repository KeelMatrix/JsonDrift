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
