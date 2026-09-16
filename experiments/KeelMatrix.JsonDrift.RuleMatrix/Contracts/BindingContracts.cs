namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R12 - constructor binding
internal sealed record ShipmentEventV1(string Id);

internal sealed record ShipmentEventV2(string Id, string Carrier);

internal sealed record ShipmentEventV2Defaulted(string Id, string Carrier = "unspecified");

internal sealed record QuoteV1(decimal Value);

internal sealed record QuoteV2(decimal Amount);
