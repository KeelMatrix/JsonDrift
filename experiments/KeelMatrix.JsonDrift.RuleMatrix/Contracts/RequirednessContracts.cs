using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R04 - requiredness
internal sealed class ShipmentV1
{
    public int Id { get; set; }

    public string? Tracking { get; set; }
}

internal sealed class ShipmentRequired
{
    public int Id { get; set; }

    [JsonRequired]
    public string? Tracking { get; set; }
}

internal sealed class ShipmentRequiredKeyword
{
    public int Id { get; set; }

    public required string? Tracking { get; set; }
}

// R05 - nullability
internal sealed class ProfileV1Nullable
{
    public string? Name { get; set; }
}

internal sealed class ProfileV2NonNullable
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class CounterV1NonNullable
{
    public int Quantity { get; set; }
}

internal sealed class CounterV2Nullable
{
    public int? Quantity { get; set; }
}

internal sealed class CounterV1Nullable
{
    public int? Quantity { get; set; }
}

internal sealed class CounterV2NonNullable
{
    public int Quantity { get; set; }
}
