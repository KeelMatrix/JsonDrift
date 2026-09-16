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
internal sealed partial class TelemetryContext : JsonSerializerContext
{
}
