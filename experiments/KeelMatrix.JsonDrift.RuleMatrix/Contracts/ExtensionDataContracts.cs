using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R11 - extension data
internal sealed class EnvelopeV1
{
    public int Id { get; set; }
}

internal sealed class EnvelopeWithExtensionData
{
    public int Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class EnvelopeV2WithoutExtensionData
{
    public int Id { get; set; }
}
