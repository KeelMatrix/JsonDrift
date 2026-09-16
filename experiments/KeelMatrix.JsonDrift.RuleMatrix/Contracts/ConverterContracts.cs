using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R14 - converter-driven and opaque metadata
internal sealed class Temperature
{
    public decimal Degrees { get; set; }
}

internal sealed class TemperatureConverter : JsonConverter<Temperature>
{
    public override Temperature Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Degrees = reader.GetDecimal() };

    public override void Write(Utf8JsonWriter writer, Temperature value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Degrees);
}

internal sealed class ReadingV2Opaque
{
    public int Id { get; set; }

    [JsonConverter(typeof(TemperatureConverter))]
    public Temperature Value { get; set; } = new();
}

internal sealed class Money
{
    public decimal Amount { get; set; }
}

internal sealed class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Amount = reader.GetDecimal() };

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Amount);
}

internal sealed class WalletV1
{
    public decimal Balance { get; set; }
}

internal sealed class WalletV2
{
    public Money Balance { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum Priority
{
    Low = 0,
    High = 1,
}

internal sealed class PriorityHolder
{
    public Priority Priority { get; set; }
}

[JsonConverter(typeof(PriceTagConverter))]
internal sealed class PriceTag
{
    public decimal Amount { get; set; }
}

internal sealed class PriceTagConverter : JsonConverter<PriceTag>
{
    public override PriceTag Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Amount = reader.GetDecimal() };

    public override void Write(Utf8JsonWriter writer, PriceTag value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Amount);
}

// Used by the determinism checks
internal sealed class SequenceProbe
{
    public Money Amount { get; set; } = new();

    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// A metadata source that replaces a member converter without declaring it on the member itself.
/// </summary>
internal sealed class ConverterInjectingResolver : DefaultJsonTypeInfoResolver
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        JsonTypeInfo typeInfo = base.GetTypeInfo(type, options);

        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(decimal))
            {
                property.CustomConverter = new MoneyConverter();
            }
        }

        return typeInfo;
    }
}
