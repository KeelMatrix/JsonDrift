using System.Globalization;

// This fixture declares an application converter inside the framework namespace prefix. Converter
// provenance has to be decided by assembly identity; a namespace prefix proves nothing.
namespace System.Text.Json.ProbeShadow;

internal sealed class ShadowProbeMoney
{
    public decimal Amount { get; set; }
}

internal sealed class ShadowProbeMoneyConverter : System.Text.Json.Serialization.JsonConverter<ShadowProbeMoney>
{
    public override ShadowProbeMoney Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        new() { Amount = decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture) };

    public override void Write(Utf8JsonWriter writer, ShadowProbeMoney value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Amount.ToString(CultureInfo.InvariantCulture));
}
