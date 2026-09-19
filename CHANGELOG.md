# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Best-effort activation and weekly heartbeat integration through `KeelMatrix.Telemetry`, with comparison-only
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

### Fixed

- Package inspection now allowlists only the generated `nuget.psmdcp` or 32-character hexadecimal core-properties
  entry and rejects unexpected `.psmdcp` names and other artifact mutations.

## [0.1.0] - 2026-09-18

### Added

- Initial `net8.0` foundation for extracting deterministic `System.Text.Json` contract baselines.
- Explicit canonical baseline creation, replacement, bounded reading, and `ReaderBackward` policy definition.
- Deny-by-default support classification with deterministic format and safe parsing limits.
