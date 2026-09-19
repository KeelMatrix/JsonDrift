# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Deterministic `ReaderBackward` contract comparison with complete structured change reports.
- `JsonDrift.Compare`, `JsonDriftReport.AssertCompatible()`, and fail-closed diagnostics for incompatible or
  unsupported metadata.
- Rule-matrix coverage through the shipping comparison and assertion types.

## [0.1.0] - 2026-09-18

### Added

- Initial `net8.0` foundation for extracting deterministic `System.Text.Json` contract baselines.
- Explicit canonical baseline creation, replacement, bounded reading, and `ReaderBackward` policy definition.
- Deny-by-default support classification with deterministic format and safe parsing limits.
