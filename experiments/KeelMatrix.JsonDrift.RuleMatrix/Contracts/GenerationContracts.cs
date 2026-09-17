using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R13 - source-generated context metadata versus reflection metadata
internal sealed class TelemetryEvent
{
    public string Name { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public int Count { get; set; }
}

internal sealed class TelemetryBatch
{
    public string BatchId { get; set; } = string.Empty;

    public List<TelemetryEvent> Events { get; set; } = new();
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TelemetryEvent))]
[JsonSerializable(typeof(TelemetryBatch))]
[JsonSerializable(typeof(ShipmentV1))]
[JsonSerializable(typeof(ShipmentRequired))]
[JsonSerializable(typeof(ProfileV1Nullable))]
[JsonSerializable(typeof(ProfileV2NonNullable))]
[JsonSerializable(typeof(StrictNumberType))]
[JsonSerializable(typeof(StrictNumberMember))]
[JsonSerializable(typeof(WriteAsStringNumberType))]
[JsonSerializable(typeof(WriteAsStringNumberMember))]
[JsonSerializable(typeof(NeverIgnoredMember))]
[JsonSerializable(typeof(DefaultIgnoredMember))]
[JsonSerializable(typeof(RedundantJsonConstructorWithoutAttribute))]
[JsonSerializable(typeof(RedundantJsonConstructorWithAttribute))]
[JsonSerializable(typeof(ConstructorBindingWithoutAttribute))]
[JsonSerializable(typeof(ConstructorBindingWithAttribute))]
[JsonSerializable(typeof(RuntimeSerializationAttributeHolder))]
internal sealed partial class TelemetryContext : JsonSerializerContext
{
}
