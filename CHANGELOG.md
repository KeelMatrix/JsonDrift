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
  `System.Text.Json` behavior. Measured allowlisted `[JsonPropertyName]` renames are normally incompatible; an
  optional destination can use the measured extension-data preservation exception only after its independent
  presence constraints and the earlier extension-data key space are accounted for. Arbitrary unallowlisted
  serialization attributes or values, including unmeasured renames, are unsupported.
- Rule-matrix coverage through the shipping comparison and assertion types.
- Canonical baseline format 5 records complete nested object, collection-element, and dictionary key/value
  contract graphs, including concrete object and dictionary construction capability and whether a polymorphic base requires a type
  discriminator for reader materialization, with bounded references for recursive types.
- Root scalar token changes, nested contract changes, and evidenced numeric range, signedness, fractional, and
  precision-loss transitions are classified fail-closed by the shipping comparison; unclassified scalar and
  numeric transitions are unsupported.
- Public reflection and source-generated regressions cover nested members, deeper graphs, collection elements,
  dictionary values, root scalar tokens, and boundary-valued numeric witnesses through `Compare` and
  `AssertCompatible`.
- A recorded-fact comparison rule table binds every traversed fact to a measured preservation witness or an
  explicit non-contract reason; an automated gate rejects uncovered facts and every rule gap fails closed.
- A pairwise contract-fact interaction table classifies every family pair with a measured witness or an explicit
  wire-level non-interaction reason; its gate rejects generic or internal-layout justifications and a newly
  introduced family until every pair is classified.
- Nullable value slots are retained at roots and nested edges, writable-member materialization is checked,
  collection compatibility is restricted to order-and-multiplicity-preserving materializers, enum integer-token
  acceptance is compared, abstract and interface readers without registered polymorphic derived types are
  unsupported, independent member constraints are all reported, and malformed reference graphs are rejected
  before comparison.
- The root and package READMEs now contain a complete source-generated first-success example with explicit
  baseline creation, a compatible change, and a rejected change; validation runs that same example from the
  built package.
- Initial `net8.0` foundation for extracting deterministic `System.Text.Json` contract baselines.
- Explicit canonical baseline creation, replacement, bounded reading, and `ReaderBackward` policy definition.
- Deny-by-default support classification with deterministic format and safe parsing limits.

### Fixed

- Adding polymorphic dispatch to an object with earlier writable extension data now reports `Unsupported` when
  the later discriminator property overlaps the earlier arbitrary key space, preventing unknown discriminator
  values from being accepted as reader-backward compatible.
- Required property renames are no longer accepted merely because later extension data captures the old key;
  destination requiredness and constructor-presence constraints are evaluated first.
- Optional member additions and rename destinations now fail closed when an earlier extension-data key/value
  space can collide with the new name and supply an incompatible token.
- Dictionary support and comparison now require a measured construction path, so concrete read-only or otherwise
  unproven dictionary materializers report `Unsupported` at roots and nested writable members.
- Concrete object support now requires a measured public parameterless, single fully-bound public parameterized,
  or fully-bound public `[JsonConstructor]` path; private-constructor-only and ambiguous construction paths report
  `Unsupported` at roots and nested writable members.
- Polymorphic discriminator property names that collide with effective ordinary member names now report
  `Unsupported`, preventing ordinary member data from being reinterpreted as dispatch metadata.
- Package inspection now allowlists only the generated `nuget.psmdcp` or 32-character hexadecimal core-properties
  entry and rejects unexpected `.psmdcp` names and other artifact mutations.
- Release validation now checks the tag, package version, finalized changelog, exact package and symbol archives,
  isolated package consumption, and tag-only Trusted Publishing workflow before publication.
- Release validation rejects impossible calendar dates and non-`Added` categories in the `0.1.0` first-release
  entry, with positive and negative contract fixtures.
