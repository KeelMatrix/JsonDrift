using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R09 - ignored and included members
internal sealed class SessionV1
{
    public int Id { get; set; }

    public string Secret { get; set; } = string.Empty;
}

internal sealed class SessionV2Ignored
{
    public int Id { get; set; }

    [JsonIgnore]
    public string Secret { get; set; } = string.Empty;
}

internal sealed class NoteV1
{
    public int Id { get; set; }

    public string? Note { get; set; }
}
