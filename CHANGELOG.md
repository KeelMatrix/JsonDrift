# Changelog

All notable changes to this project are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-09-25

### Added

- Extracts a deterministic, versioned canonical `System.Text.Json` contract from effective `JsonTypeInfo`, reflection-backed `JsonSerializerOptions`, or source-generated metadata.
- Provides explicit baseline creation, replacement, and bounded reading using stable canonical JSON with recursive contract references, deterministic ordering, UTF-8 encoding without a byte-order mark, and LF line endings; comparisons never rewrite baselines.
- Compares contracts with the `JsonCompatibility.ReaderBackward` policy and produces deterministic structured reports with paths, classifications, rule identifiers, and reasons; `JsonDriftReport.AssertCompatible()` fails for incompatible or unsupported changes.
- Classifies measured structural evolution across members, names, requiredness, nullable value acceptance, token and numeric forms, collections and dictionaries, enums, constructors and bindings, extension data, polymorphic metadata, and serializer options; opaque or unmeasured metadata remains unsupported.
- Integrates best-effort activation and heartbeat telemetry after real baseline comparisons, with opt-out controls, failure isolation, and no transmission of contract content or comparison payloads.
