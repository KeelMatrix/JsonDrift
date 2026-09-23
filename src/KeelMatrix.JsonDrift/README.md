# KeelMatrix.JsonDrift

`KeelMatrix.JsonDrift` extracts the effective `System.Text.Json` metadata used by a .NET application into a deterministic, reviewable structural contract baseline and compares later metadata against it. It is designed for tests and CI, and reports a contract as unsupported when a serializer feature cannot be classified safely.

Version 1 targets `net8.0` and ships the `ReaderBackward` compatibility policy only. Reader-backward means that documented JSON wire data accepted by the earlier contract remains readable without loss under the later contract. It does not claim source/API compatibility or business/semantic compatibility.

The package targets `net8.0`; the committed repository validation gate runs on Windows, Linux, and macOS for the
pinned SDK/runtime combination. Other runtime and SDK combinations are outside the validated support claim.

## Install

Create a `net8.0` console project and install it with:

```pwsh
dotnet new console --framework net8.0 --name JsonDriftExample
Set-Location JsonDriftExample
dotnet add package KeelMatrix.JsonDrift --version 0.1.0
```

## Quick Start

Save the following complete program as `Program.cs`. It includes the DTOs, imports, and source-generated
context used by the example.

<!-- BEGIN:FIRST-SUCCESS-EXAMPLE -->
```csharp
using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift;

const string baselinePath = "contracts/order-event.json";

if (args.Length != 1)
{
    throw new ArgumentException("Choose create-baseline, compatible, or breaking.");
}

switch (args[0])
{
    case "create-baseline":
        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        JsonBaseline.Create(
            OrderEventJsonContext.Default.OrderEventV1,
            baselinePath,
            overwrite: false);
        Console.WriteLine($"baseline created: {baselinePath}");
        break;

    case "compatible":
        JsonDriftReport compatible = JsonDrift.Compare(
            OrderEventJsonContext.Default.OrderEventV2WithOptionalNote,
            baselinePath,
            JsonCompatibility.ReaderBackward);
        compatible.AssertCompatible();
        Console.WriteLine($"compatible change: {compatible.Outcome}");
        break;

    case "breaking":
        JsonDriftReport breaking = JsonDrift.Compare(
            OrderEventJsonContext.Default.OrderEventV2RenamedOrRequired,
            baselinePath,
            JsonCompatibility.ReaderBackward);
        Console.WriteLine($"rejected change: {breaking.Outcome}");
        foreach (JsonDriftChange change in breaking.Changes)
        {
            Console.WriteLine($"- {change.RuleId}: {change.Reason}");
        }

        try
        {
            breaking.AssertCompatible();
            throw new InvalidOperationException("the breaking change was accepted");
        }
        catch (JsonDriftCompatibilityException)
        {
            Console.WriteLine("AssertCompatible rejected the change.");
        }

        break;

    default:
        throw new ArgumentException("Choose create-baseline, compatible, or breaking.");
}

public sealed class OrderEventV1
{
    public int OrderId { get; set; }

    [JsonPropertyName("account_id")]
    public string? CustomerName { get; set; }
}

public sealed class OrderEventV2WithOptionalNote
{
    public int OrderId { get; set; }

    [JsonPropertyName("account_id")]
    public string? CustomerName { get; set; }

    public string? Note { get; set; }
}

public sealed class OrderEventV2RenamedOrRequired
{
    public int OrderId { get; set; }

    [JsonPropertyName("accountId")]
    public string? CustomerName { get; set; }
}

[JsonSerializable(typeof(OrderEventV1))]
[JsonSerializable(typeof(OrderEventV2WithOptionalNote))]
[JsonSerializable(typeof(OrderEventV2RenamedOrRequired))]
public partial class OrderEventJsonContext : JsonSerializerContext
{
}
```
<!-- END:FIRST-SUCCESS-EXAMPLE -->

Create the baseline once and commit `contracts/order-event.json`:

```pwsh
dotnet run -- create-baseline
```

For routine comparisons, do not recreate or rewrite the baseline. The optional member is compatible:

```pwsh
dotnet run -- compatible
```

The serialized-name change is rejected with a structured report, and `AssertCompatible()` throws:

```pwsh
dotnet run -- breaking
```

A reflection/options comparison is also supported. In a complete program, supply the application's
`JsonSerializerOptions` with an explicit `DefaultJsonTypeInfoResolver` and call the corresponding generic
overloads; the source-generated program above is the complete first-success path.

A measured, allowlisted `[JsonPropertyName]` rename is normally incompatible. It is compatible through later
`[JsonExtensionData]` only when the destination is optional, has no enforced constructor presence constraint,
and the earlier contract has no extension-data key space that can shadow the destination name. A required
renamed destination remains incompatible; an earlier extension-data collision is unsupported. An arbitrary
unallowlisted serialization attribute or value, including an unmeasured rename, is unsupported.

Adding polymorphic dispatch to an object with earlier writable extension data is unsupported. The earlier
arbitrary key space can already contain the later discriminator property name with a value that the later
reader does not recognize; `Compare` reports `R10c.polymorphism.extension-data-discriminator-collision`
instead of accepting that transition.

A discriminator property name that collides with an effective ordinary serialized member on the declaring
contract or anywhere in its reachable registered derived hierarchy is also unsupported. This includes inherited
and renamed included members; ignored members are not on the wire. Both values occupy the same JSON property
name, so the later reader can consume ordinary member data as dispatch metadata; `Compare` reports
`unsupported.polymorphism-discriminator-member-collision`.

A concrete non-polymorphic contract becoming an abstract or interface polymorphic contract is incompatible:
earlier documents contain no type discriminator, and the later reader cannot materialize the declared base.
`Compare` reports `R10d.polymorphism.discriminator-required`. Adding dispatch while the base stays concrete
retains the measured compatible control under `R10e.polymorphism.dispatch-added-concrete` only when the
discriminator name does not collide with an ordinary member anywhere in that hierarchy.

A plain abstract or interface contract without registered polymorphic derived-type metadata is unsupported.
The framework reader cannot materialize that declared type, so `Compare` reports
`unsupported.object-materialization-unproven` instead of accepting an unchanged member shape.

Concrete objects also require measured construction evidence. A public parameterless constructor, one fully
bound public parameterized constructor, or one fully bound public constructor selected with `[JsonConstructor]`
is supported. A private-constructor-only class or an ambiguous constructor set reports
`unsupported.object-materialization-unproven`, including when reached through a writable member.

`JsonDrift.Compare` also accepts an already-extracted `JsonContract`. After an intentional change, use
`JsonBaseline.Update(contract, path, overwrite: true)` explicitly; comparison never rewrites a baseline.
Reading never rewrites a baseline.

Canonical baseline documents use `formatVersion: 5`. The format records complete nested object, collection
element, and dictionary key/value contract nodes plus object and dictionary construction capability, while repeated types
use validated bounded references so recursive contracts terminate. Polymorphic records include whether the
declared base requires a discriminator for reader materialization. Nullable value slots, member materialization
capability, collection order and multiplicity semantics, and enum integer-token acceptance are recorded and
compared. Collection compatibility
is limited to measured materializers such as `List<T>` and arrays; other enumerable materializers are
unsupported. Dictionary support likewise requires a measured concrete materializer: `Dictionary<TKey,TValue>`
is supported, while an unproven concrete type such as `ReadOnlyDictionary<TKey,TValue>` is unsupported even
when key and value contracts match. Scalar nodes record their JSON token kind. Malformed, foreign, unsupported,
future,
oversized, and over-depth documents are rejected. Canonical bytes are UTF-8 without a BOM, LF-terminated,
stable in ordering, and contain no timestamps or host paths.

Unsupported metadata is deny-by-default and includes a diagnostic naming the offending declaration or feature where available. Custom converters are not executed to reverse-engineer behavior.

Comparison is fail-closed at the root and throughout the nested graph: token changes are incompatible, explicit
numeric widening and narrowing rules are used for numeric type changes, and unclassified scalar or numeric
transitions are unsupported. `Compare` and `AssertCompatible` apply the same result to nested object members,
collection elements, and dictionary values.

## Telemetry and privacy

The comparison core is offline. A real comparison against an accepted baseline may make a best-effort request
to `KeelMatrix.Telemetry`; baseline creation alone does not activate telemetry, and JsonDrift sends no
product-specific comparison payload. Telemetry failure cannot change comparison or assertion results. The shared
telemetry package owns its event fields, pseudonymous identifiers, delivery, retention, and opt-out precedence.
The JsonDrift-specific boundary is in the [privacy policy](https://github.com/KeelMatrix/JsonDrift/blob/main/PRIVACY.md).

## Deeper documentation

- [Repository README](https://github.com/KeelMatrix/JsonDrift/blob/main/README.md)
- [Compatibility rules](https://github.com/KeelMatrix/JsonDrift/blob/main/docs/compatibility-rules.md)
- [Initial-release scope](https://github.com/KeelMatrix/JsonDrift/blob/main/docs/initial-release-scope.md)
- [Security policy](https://github.com/KeelMatrix/JsonDrift/blob/main/SECURITY.md)
- [Privacy policy](https://github.com/KeelMatrix/JsonDrift/blob/main/PRIVACY.md)
