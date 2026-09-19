# KeelMatrix.JsonDrift

`KeelMatrix.JsonDrift` extracts the effective `System.Text.Json` metadata used by a .NET application into a deterministic, reviewable structural contract baseline and compares later metadata against it. It is designed for tests and CI, and reports a contract as unsupported when a serializer feature cannot be classified safely.

Version 1 targets `net8.0` and ships the `ReaderBackward` compatibility policy only. Reader-backward means that documented JSON wire data accepted by the earlier contract remains readable without loss under the later contract. It does not claim source/API compatibility or business/semantic compatibility.

Install it with:

```pwsh
dotnet add package KeelMatrix.JsonDrift --version 0.1.0
```

Use the actual metadata selected by the application to create a baseline and compare later versions:

```csharp
JsonTypeInfo<OrderEventV1> baselineContract = MyJsonContext.Default.OrderEventV1;
const string path = "contracts/order-event.json";

JsonBaseline.Create(baselineContract, path, overwrite: false);

// An optional additive member passes:
JsonTypeInfo<OrderEventV2WithOptionalNote> additiveContract = MyJsonContext.Default.OrderEventV2WithOptionalNote;
JsonDriftReport additive = JsonDrift.Compare(additiveContract, path, JsonCompatibility.ReaderBackward);
additive.AssertCompatible();

// A measured allowlisted rename or newly required member fails with structured changes:
JsonTypeInfo<OrderEventV2RenamedOrRequired> breakingContract = MyJsonContext.Default.OrderEventV2RenamedOrRequired;
JsonDriftReport breakingChange = JsonDrift.Compare(breakingContract, path, JsonCompatibility.ReaderBackward);
// breakingChange.Changes contains the path, classification, rule, and reason.
// breakingChange.AssertCompatible() throws JsonDriftCompatibilityException.
```

The reflection/options overload requires an explicit resolver with the pinned `System.Text.Json` behavior:

```csharp
JsonSerializerOptions options = new()
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

JsonBaseline.Create<OrderEventV1>(options, path, overwrite: false);
JsonDriftReport reflectionComparison = JsonDrift.Compare<OrderEventV2WithOptionalNote>(
    options, path, JsonCompatibility.ReaderBackward);
```

A measured, allowlisted `[JsonPropertyName]` rename produces an incompatible change. An arbitrary
unallowlisted serialization attribute or value, including an unmeasured rename, is unsupported.

`JsonDrift.Compare` also accepts an already-extracted `JsonContract`. After an intentional change, use
`JsonBaseline.Update(contract, path, overwrite: true)` explicitly; comparison never rewrites a baseline.
Reading never rewrites a baseline.

Canonical baseline documents use `formatVersion: 1`. The format is versioned; malformed, foreign, unsupported, future, oversized, and over-depth documents are rejected. Canonical bytes are UTF-8 without a BOM, LF-terminated, stable in ordering, and contain no timestamps or host paths.

Unsupported metadata is deny-by-default and includes a diagnostic naming the offending declaration or feature where available. Custom converters are not executed to reverse-engineer behavior.

## Deeper documentation

- [Repository README](https://github.com/KeelMatrix/JsonDrift/blob/main/README.md)
- [Compatibility rules](https://github.com/KeelMatrix/JsonDrift/blob/main/docs/compatibility-rules.md)
- [Initial-release scope](https://github.com/KeelMatrix/JsonDrift/blob/main/docs/initial-release-scope.md)
- [Security policy](https://github.com/KeelMatrix/JsonDrift/blob/main/SECURITY.md)
- [Privacy policy](https://github.com/KeelMatrix/JsonDrift/blob/main/PRIVACY.md)
