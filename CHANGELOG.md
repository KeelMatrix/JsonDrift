# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Best-effort activation and heartbeat integration through `KeelMatrix.Telemetry`, with comparison-only
  activation semantics, the shared client's standard event contract, process opt-out handling, and failure isolation.
- Deterministic `ReaderBackward` contract comparison with complete structured change reports.
- `JsonDrift.Compare`, `JsonDriftReport.AssertCompatible()`, and fail-closed diagnostics for incompatible or
  unsupported metadata.
- Complete reports for effective ignored/included member transitions and constructor-binding changes, including
  defaulted, enforced, and renamed bindings.
- The reflection/options path requires an explicit `DefaultJsonTypeInfoResolver` with the pinned
  `System.Text.Json` behavior. Measured allowlisted `[JsonPropertyName]` renames are incompatible; arbitrary
  unallowlisted serialization attributes or values, including unmeasured renames, are unsupported.
- Rule-matrix coverage through the shipping comparison and assertion types.
- Canonical baseline format 2 records complete nested object, collection-element, and dictionary key/value
  contract graphs with bounded references for recursive types.
- Root scalar token changes, nested contract changes, and evidenced numeric range, signedness, fractional, and
  precision-loss transitions are classified fail-closed by the shipping comparison; unclassified scalar and
  numeric transitions are unsupported.
- Public reflection and source-generated regressions cover nested members, deeper graphs, collection elements,
  dictionary values, root scalar tokens, and boundary-valued numeric witnesses through `Compare` and
  `AssertCompatible`.
- A recorded-fact comparison rule table binds every traversed fact to a measured preservation witness or an
  explicit non-contract reason; an automated gate rejects uncovered facts and every rule gap fails closed.
- Nullable value slots are retained at roots and nested edges, writable-member materialization is checked,
  collection compatibility is restricted to order-and-multiplicity-preserving materializers, enum integer-token
  acceptance is compared, independent member constraints are all reported, and malformed reference graphs are
  rejected before comparison.

### Fixed

- Package inspection now allowlists only the generated `nuget.psmdcp` or 32-character hexadecimal core-properties
  entry and rejects unexpected `.psmdcp` names and other artifact mutations.
- Release validation now checks the tag, package version, finalized changelog, exact package and symbol archives,
  isolated package consumption, and tag-only Trusted Publishing workflow before publication.

## [0.1.0] - 2026-09-18

### Added

- Initial `net8.0` foundation for extracting deterministic `System.Text.Json` contract baselines.
- Explicit canonical baseline creation, replacement, bounded reading, and `ReaderBackward` policy definition.
- Deny-by-default support classification with deterministic format and safe parsing limits.
