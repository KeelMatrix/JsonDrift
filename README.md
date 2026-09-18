# KeelMatrix.JsonDrift

KeelMatrix.JsonDrift extracts a deterministic, versioned description of an effective
`System.Text.Json` contract and stores it as an explicit baseline. Phase 1-A1 ships the
foundation library only; comparison, reporting, and assertion APIs are deferred to slice A2.

## A1 scope

- Package: `KeelMatrix.JsonDrift`, targeting `net8.0`.
- Compatibility policy: `JsonCompatibility.ReaderBackward` only. It describes documented
  JSON wire readability and lossless reading of an earlier document; it does not claim source
  or API compatibility, and it cannot prove business or semantic compatibility.
- Unsupported or unclassified metadata is deny-by-default and is never represented as compatible.
- Extraction accepts `JsonTypeInfo`, reflection-backed `JsonSerializerOptions`, and source-generated
  type information. Application converters are not executed to reverse-engineer behavior.
- Baselines use canonical JSON format version `1`: UTF-8 without BOM, LF line endings, stable ordering,
  no timestamps or host paths, bounded parsing, and explicit create/update/read operations.

The measured classification rules and their evidence remain in
[docs/compatibility-rules.md](docs/compatibility-rules.md). The initial-release decisions are in
[docs/initial-release-scope.md](docs/initial-release-scope.md).

## Build and test

```pwsh
dotnet restore KeelMatrix.JsonDrift.sln --configfile NuGet.config
dotnet build KeelMatrix.JsonDrift.sln -c Release --no-restore
dotnet test KeelMatrix.JsonDrift.sln -c Release --no-build
pwsh scripts/validate.ps1
```

The local validation script also runs the promoted Phase 0 matrix, checks canonical bytes in two
independent processes, and verifies its required-check manifest fail-closed.

The package is local/offline at runtime. It has no CLI, hosted client, registry integration, Kafka
integration, or JSON-Schema exporter in this slice.
