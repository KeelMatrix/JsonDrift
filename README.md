# KeelMatrix JsonDrift

KeelMatrix.JsonDrift turns the `System.Text.Json` contract an application actually uses into a deterministic,
reviewable baseline and checks later DTO or serializer changes against an explicit compatibility policy. It
fails on structural wire incompatibility - and on any change it cannot classify safely - instead of silently
treating unknown behavior as compatible.

The package is not published yet. This repository currently contains the compatibility-semantics work that
defines what the package may claim; the public API, baseline format, and package are not implemented here.

## Why the contract is not the class

A C# type change is not the same thing as a JSON contract change. Effective `System.Text.Json` metadata
decides serialized names, member presence, requiredness, nullability, token kinds, collection shapes, enum
representation, ignored members, captured members, constructor binding, and polymorphism. JsonDrift derives
its contract from that effective metadata - `JsonTypeInfo` from a source-generated context or from
`JsonSerializerOptions` - rather than from C# reflection alone or from captured sample payloads.

## Compatibility semantics

[docs/compatibility-rules.md](docs/compatibility-rules.md) is the normative rule matrix. It defines
`ReaderBackward`, `WriterForward`, and `Full` in terms of wire readability, records the classification of
every supported change under each policy, and names the executed proof for each one. It also states the
explicit unsupported handling: an unrecognized converter or metadata source is reported as unsupported and
never as compatible. Classification is deny by default: a contract is supported only when a single recursive
traversal recorded the shape evidence behind it and its converter, resolver, and serializer-option metadata
matches explicit allowlists, so a metadata path the walk missed, a converter it does not recognize, and a
serializer option value the committed checks were not measured under are all reported as unsupported instead
of as compatible, and the canonical document records the option values the contract was recorded under.

[docs/initial-release-scope.md](docs/initial-release-scope.md) records the recommended first-release scope and
the measured evidence behind it.

## Repository layout

```text
experiments/KeelMatrix.JsonDrift.RuleMatrix   executable compatibility-rule matrix, not packable
docs/compatibility-rules.md                   normative classification rules and their evidence
docs/initial-release-scope.md                 first-release scope and the evidence behind it
scripts/validate-rule-matrix.ps1              restore, build, run the matrix, compare canonical output
```

## Validation

```pwsh
pwsh scripts/validate-rule-matrix.ps1
```

The script restores and builds the solution in Release, runs the rule matrix (which fails when any measured
classification disagrees with the recorded one), writes the canonical contract document for the
representative root contract twice in separate processes, and compares the two files byte for byte.

The matrix can also be run directly:

```pwsh
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release -- --matrix --verbose
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release -- --canonical artifacts/canonical
```

Requirements: .NET SDK 8.0.4xx and the `System.Text.Json` 10.0.12 package restored from nuget.org.

## Privacy

Contract extraction and comparison are local and offline. Contract documents and baselines contain
source-code-level information and are never transmitted. See [PRIVACY.md](PRIVACY.md) and
[SECURITY.md](SECURITY.md).
