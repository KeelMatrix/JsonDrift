# KeelMatrix.JsonDrift

KeelMatrix.JsonDrift extracts a deterministic, versioned description of an effective
`System.Text.Json` contract, stores it as an explicit baseline, and compares later metadata with the
`ReaderBackward` policy. Reports contain every classified change and fail closed for unsupported metadata.

## Install

Create a `net8.0` console project and install the package:

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

For routine comparisons, do not recreate or rewrite the baseline. An optional member is compatible:

```pwsh
dotnet run -- compatible
```

A serialized-name change is rejected with a structured report, and `AssertCompatible()` throws:

```pwsh
dotnet run -- breaking
```

The measured classification rules and their evidence remain in
[docs/compatibility-rules.md](docs/compatibility-rules.md). The initial-release decisions are in
[docs/initial-release-scope.md](docs/initial-release-scope.md).

## Scope

- Package: `KeelMatrix.JsonDrift`, targeting `net8.0`.
- Compatibility policy: `JsonCompatibility.ReaderBackward` only. It describes documented
  JSON wire readability and lossless reading of an earlier document; it does not claim source
  or API compatibility, and it cannot prove business or semantic compatibility.
- Unsupported or unclassified metadata is deny-by-default and is never represented as compatible.
- Extraction accepts `JsonTypeInfo`, reflection-backed `JsonSerializerOptions`, and source-generated
  type information. Application converters are not executed to reverse-engineer behavior.
- Baselines use canonical JSON format version `5`: UTF-8 without BOM, LF line endings, stable ordering,
  no timestamps or host paths, bounded parsing, and explicit create/update/read operations.
- Canonical nodes retain complete nested object, collection-element, and dictionary key/value contracts plus
  dictionary construction capability; repeated types use validated bounded references. Nullable value slots
  are preserved at roots and nested
  edges; collection compatibility is limited to materializers proven to preserve order and multiplicity;
  member and containing-object materialization and enum integer-token acceptance are compared explicitly. A
  concrete object is supported only with a measured public parameterless, single fully-bound public
  parameterized, or fully-bound public `[JsonConstructor]` construction path. Root scalar token changes
  are incompatible, proven numeric range or precision loss is incompatible, and unclassified transitions are
  unsupported.
- An optional `[JsonPropertyName]` rename is compatible through later `[JsonExtensionData]` only when the
  destination has no independent presence constraint and the earlier contract has no extension-data key space
  that can shadow the new name. Required rename destinations remain incompatible; additions or renames that
  collide with an earlier extension-data key space are unsupported. Adding polymorphic dispatch to an object
  with earlier writable extension data is also unsupported because the earlier key space can contain the new
  discriminator name with an unrecognized discriminator value. A discriminator name that collides with an
  effective ordinary member name is unsupported because the same JSON property can be consumed as dispatch
  metadata instead of member data. A concrete contract becoming an abstract or
  interface polymorphic contract is incompatible because the later reader requires a discriminator that
  earlier documents do not contain; adding dispatch while the declared base stays concrete retains the measured
  compatible control when its discriminator name does not collide with an ordinary member. A plain abstract or
  interface contract without registered polymorphic derived-type metadata is unsupported because the reader
  cannot materialize it. Concrete objects without a measured constructor path and concrete dictionary types without a measured
  construction path are unsupported even when their key and value contracts match.
- `JsonDrift.Compare` compares current `JsonTypeInfo` or serializer options with a baseline path or extracted
  `JsonContract`; `JsonDriftReport.AssertCompatible()` throws `JsonDriftCompatibilityException` for an
  incompatible or unsupported result.

## Supported platform

The package targets `net8.0`. The repository validation gate runs on Windows, Linux, and macOS through the
committed GitHub Actions matrix for the pinned SDK/runtime combination. Other runtime and SDK combinations are
outside the validated support claim.

## Validation evidence

The repository-controlled `scripts/validate.ps1` gate is the source of truth for restore, Release build, tests,
package and symbol inspection, isolated consumer smoke, deterministic-output checks, release-contract
regressions, and the direct-and-transitive vulnerability audit. The same gate runs in committed CI on
`windows-latest`, `ubuntu-latest`, and `macos-latest`.

## Release process

Pushing a `vMAJOR.MINOR.PATCH` tag runs the committed release workflow. It revalidates the tag, finalized
changelog, Release build, exact `.nupkg`/`.snupkg` set, symbol metadata, and package-consumer evidence before
publishing through NuGet Trusted Publishing as `dmitriyzen`; the corresponding GitHub Release is created only
after publication succeeds. The repository requires the NuGet trusted-publisher configuration for this workflow;
it does not use a long-lived NuGet API-key secret.

## Telemetry and privacy

The extraction and comparison core is offline and does not require network access. After a real comparison
against an accepted baseline, JsonDrift makes a best-effort request to the shared `KeelMatrix.Telemetry`
client for activation and heartbeat signals; baseline creation alone is not an activation. JsonDrift sends no
product-specific comparison payload, and telemetry failure never changes a report or assertion result. The
shared telemetry package owns event fields, pseudonymous identifiers, delivery, retention, and opt-out
precedence. See [PRIVACY.md](PRIVACY.md) for the JsonDrift-specific boundary and the shared telemetry policy.

The runtime dependency graph is intentionally small: `System.Text.Json` `10.0.12` and
`KeelMatrix.Telemetry` `[0.1.1]`. The analyzer and SourceLink packages are build-only dependencies and do not
flow to consumers.

After an intentional contract change, replace the committed baseline explicitly with
`JsonBaseline.Update(contract, path, overwrite: true)`. Normal comparisons never rewrite the baseline.

## Deeper documentation

- [Repository README](https://github.com/KeelMatrix/JsonDrift/blob/main/README.md)
- [Compatibility rules](https://github.com/KeelMatrix/JsonDrift/blob/main/docs/compatibility-rules.md)
- [Initial-release scope](https://github.com/KeelMatrix/JsonDrift/blob/main/docs/initial-release-scope.md)
- [Security policy](https://github.com/KeelMatrix/JsonDrift/blob/main/SECURITY.md)
- [Privacy policy](https://github.com/KeelMatrix/JsonDrift/blob/main/PRIVACY.md)

## Build and test

```pwsh
dotnet restore KeelMatrix.JsonDrift.sln --configfile NuGet.config
dotnet build KeelMatrix.JsonDrift.sln -c Release --no-restore
dotnet test KeelMatrix.JsonDrift.sln -c Release --no-build
pwsh scripts/validate.ps1
```

The local validation script also runs the promoted rule matrix, checks canonical bytes in two independent
processes, and verifies its required-check manifest fail-closed.

The comparison core remains local/offline at runtime. The package has no CLI, hosted client, registry
integration, Kafka integration, or JSON-Schema exporter in this release.
