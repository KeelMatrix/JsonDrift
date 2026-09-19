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
- Baselines use canonical JSON format version `1`: UTF-8 without BOM, LF line endings, stable ordering,
  no timestamps or host paths, bounded parsing, and explicit create/update/read operations.
- `JsonDrift.Compare` compares current `JsonTypeInfo` or serializer options with a baseline path or extracted
  `JsonContract`; `JsonDriftReport.AssertCompatible()` throws `JsonDriftCompatibilityException` for an
  incompatible or unsupported result.

## Supported platform

The package targets `net8.0` and has been validated on Windows. Linux and macOS are not claimed for this
release because equivalent platform evidence is not available yet.

## Telemetry and privacy

The extraction and comparison core is offline and does not require network access. After a real comparison
against an accepted baseline, JsonDrift makes a best-effort request to the shared `KeelMatrix.Telemetry`
client for one activation signal and its low-frequency weekly heartbeat. Baseline creation alone is not an
activation. Telemetry failures never change a report or assertion result.

JsonDrift sends no product-specific comparison payload. The shared client emits its standard activation and
heartbeat fields: event kind, the `jsondrift` tool identifier, package and telemetry versions, schema version,
pseudonymous project and installation fingerprints, and the standard activation runtime/OS/CI/time or
heartbeat-week field. The project fingerprint is a salted hash derived from the project's location; ordinary
analytics cannot reverse it, and no raw identity is transmitted. These fields are used only to distinguish
projects/installations and count activation and repeat use. Contract content, baseline documents, raw repository
URLs, absolute paths, commit identifiers, namespaces, type/member names, converter names, enum values,
discriminator values, serializer configuration values, and exception messages are not sent. Disable telemetry
with `KEELMATRIX_NO_TELEMETRY=1`; the shared client also honors its process and repository-local controls.
See [PRIVACY.md](PRIVACY.md) for the product-specific contract.

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
integration, Kafka integration, or JSON-Schema exporter in this slice.
