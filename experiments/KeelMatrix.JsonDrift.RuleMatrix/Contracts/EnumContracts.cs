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
