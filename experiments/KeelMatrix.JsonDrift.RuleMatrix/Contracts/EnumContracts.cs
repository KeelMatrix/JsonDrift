using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R08 - enum representation
internal enum OrderState
{
    Created = 0,
    Shipped = 1,
    Cancelled = 2,
}

internal enum OrderStateRenamed
{
    Created = 0,
    Dispatched = 1,
    Cancelled = 2,
}

internal enum OrderStateExtended
{
    Created = 0,
    Packed = 1,
    Shipped = 2,
    Cancelled = 3,
}

internal sealed class StateHolderNumeric
{
    public OrderState State { get; set; }
}

internal sealed class StateHolderRenamed
{
    public OrderStateRenamed State { get; set; }
}

internal sealed class StateHolderExtended
{
    public OrderStateExtended State { get; set; }
}

// R08 - the same member written as a string whose serialized name depends on the applied naming policy
internal enum OrderStage
{
    Created = 0,
    InProgress = 1,
    Shipped = 2,
    Cancelled = 3,
}

internal sealed class StageHolder
{
    public OrderStage Stage { get; set; }
}

// R08 - the effective converter is declared on the member, not on the contract options or the enum type
internal sealed class StateHolderMemberLevel
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderState State { get; set; }
}
