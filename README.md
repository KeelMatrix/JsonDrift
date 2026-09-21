# KeelMatrix.JsonDrift

KeelMatrix.JsonDrift extracts a deterministic, versioned description of an effective
`System.Text.Json` contract, stores it as an explicit baseline, and compares later metadata with the
`ReaderBackward` policy. Reports contain every classified change and fail closed for unsupported metadata.

## Scope

- Package: `KeelMatrix.JsonDrift`, targeting `net8.0`.
- Compatibility policy: `JsonCompatibility.ReaderBackward` only. It describes documented
  JSON wire readability and lossless reading of an earlier document; it does not claim source
  or API compatibility, and it cannot prove business or semantic compatibility.
- Unsupported or unclassified metadata is deny-by-default and is never represented as compatible.
- Extraction accepts `JsonTypeInfo`, reflection-backed `JsonSerializerOptions`, and source-generated
  type information. Application converters are not executed to reverse-engineer behavior.
- Baselines use canonical JSON format version `2`: UTF-8 without BOM, LF line endings, stable ordering,
  no timestamps or host paths, bounded parsing, and explicit create/update/read operations.
- Canonical nodes retain complete nested object, collection-element, and dictionary key/value contracts;
  repeated types use validated bounded references. Nullable value slots are preserved at roots and nested
  edges; collection compatibility is limited to materializers proven to preserve order and multiplicity;
  member materialization and enum integer-token acceptance are compared explicitly. Root scalar token changes
  are incompatible, proven numeric range or precision loss is incompatible, and unclassified transitions are
  unsupported.
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
`KeelMatrix.Telemetry` `[0.1.0]`. The analyzer and SourceLink packages are build-only dependencies and do not
flow to consumers.

## Install and compare

Install the package into a `net8.0` test project:

```pwsh
dotnet add package KeelMatrix.JsonDrift --version 0.1.0
```

Use the application's actual source-generated metadata (or the reflection/options overload) to create an
explicit baseline and compare later metadata:

```csharp
JsonTypeInfo<OrderEventV1> baselineContract = MyJsonContext.Default.OrderEventV1;
const string path = "contracts/order-event.json";

JsonBaseline.Create(baselineContract, path, overwrite: false);

// An optional additive member is compatible for ReaderBackward:
JsonTypeInfo<OrderEventV2WithOptionalNote> additiveContract = MyJsonContext.Default.OrderEventV2WithOptionalNote;
JsonDriftReport additive = JsonDrift.Compare(
    additiveContract, path, JsonCompatibility.ReaderBackward);
additive.AssertCompatible();

// A measured allowlisted rename or a newly required member is reported as incompatible:
JsonTypeInfo<OrderEventV2RenamedOrRequired> breakingContract = MyJsonContext.Default.OrderEventV2RenamedOrRequired;
JsonDriftReport breakingChange = JsonDrift.Compare(
    breakingContract, path, JsonCompatibility.ReaderBackward);
// breakingChange.Changes contains the path, rule identifier, classification, and reason.
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

A measured, allowlisted `[JsonPropertyName]` rename is reported as incompatible. An arbitrary unallowlisted
serialization attribute or value, including an unmeasured rename, is reported as unsupported.

After an intentional contract change, replace the committed baseline explicitly with
`JsonBaseline.Update(contract, path, overwrite: true)`. Normal comparisons never rewrite the baseline.

The measured classification rules and their evidence remain in
[docs/compatibility-rules.md](docs/compatibility-rules.md). The initial-release decisions are in
[docs/initial-release-scope.md](docs/initial-release-scope.md).

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
