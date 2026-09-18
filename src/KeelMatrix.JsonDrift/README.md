# KeelMatrix.JsonDrift

`KeelMatrix.JsonDrift` extracts the effective `System.Text.Json` metadata used by a .NET application into a deterministic, reviewable structural contract baseline. It is designed for tests and CI, and reports a contract as unsupported when a serializer feature cannot be classified safely.

Version 1 targets `net8.0` and ships the `ReaderBackward` compatibility policy only. Reader-backward means that documented JSON wire data accepted by the earlier contract remains readable without loss under the later contract. It does not claim source/API compatibility or business/semantic compatibility.

The package has no comparison engine in this foundation slice. Use `JsonDrift.Extract` with a `JsonTypeInfo` or the real `JsonSerializerOptions` used by the application, and use `JsonBaseline.Create`, `JsonBaseline.Update(..., overwrite: true)`, and `JsonBaseline.Read` for explicit local baseline I/O. Reading never rewrites a baseline.

Canonical baseline documents use `formatVersion: 1`. The format is versioned; malformed, foreign, unsupported, future, oversized, and over-depth documents are rejected. Canonical bytes are UTF-8 without a BOM, LF-terminated, stable in ordering, and contain no timestamps or host paths.

Unsupported metadata is deny-by-default and includes a diagnostic naming the offending declaration or feature where available. Custom converters are not executed to reverse-engineer behavior.
