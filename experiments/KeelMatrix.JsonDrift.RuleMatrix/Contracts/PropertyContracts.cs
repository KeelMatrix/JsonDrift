using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R01 - property addition
internal sealed class TicketV1
{
    public int Id { get; set; }
}

internal sealed class TicketV2
{
    public int Id { get; set; }

    public string? Note { get; set; }
}

internal sealed class TicketV2Ignored
{
    public int Id { get; set; }

    [JsonIgnore]
    public string? Note { get; set; }
}

// R02 - property removal
internal sealed class CustomerV1
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

internal sealed class CustomerV2
{
    public string Name { get; set; } = string.Empty;
}

// R03 - serialized property name
internal sealed class AccountV1
{
    [JsonPropertyName("account_id")]
    public int Id { get; set; }
}

internal sealed class AccountV2
{
    [JsonPropertyName("accountId")]
    public int Id { get; set; }
}

internal sealed class AccountV2WithExtensionData
{
    [JsonPropertyName("accountId")]
    public int Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class InvoiceAmounts
{
    public decimal AmountDue { get; set; }
}

// Representative envelope used for canonical-document determinism
internal enum OrderStatus
{
    Created = 0,
    Shipped = 1,
    Cancelled = 2,
}

internal sealed class OrderLine
{
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

internal sealed class OrderEnvelope
{
    public string OrderId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public int? RetryCount { get; set; }

    public OrderStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = new();

    public OrderLine? PrimaryLine { get; set; }

    [JsonRequired]
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class TreeNode
{
    public string Name { get; set; } = string.Empty;

    public TreeNode? Parent { get; set; }
}
