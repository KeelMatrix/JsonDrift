# Privacy

JsonDrift reads `System.Text.Json` metadata and local canonical baselines. Extraction, baseline creation,
comparison, and assertion are local operations and do not require network access.

## What is collected

After a real comparison of at least one root contract against an accepted baseline, JsonDrift requests
best-effort activation and heartbeat signals. Baseline creation alone is not an activation. JsonDrift sends no
product-specific comparison payload.

The shared `KeelMatrix.Telemetry` package is the source of truth for event fields, pseudonymous identifiers,
cadence and deduplication, delivery behavior, retention, runtime metadata, and opt-out handling.
See the [KeelMatrix.Telemetry privacy documentation](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md)
for those shared implementation details.

## What is not sent

Contract and baseline documents never leave the machine. JsonDrift never transmits a raw repository URL, path,
commit identifier, type or member name, enum label, discriminator value, converter name, serializer configuration
value, project-file content, contract content, baseline content, or arbitrary exception message. Local diagnostics
may contain developer-facing contract detail because they stay on the machine.

## Controls and failure handling

The shared telemetry client applies the process opt-out controls and owns best-effort failure handling. JsonDrift
retains only the eligibility rule that a real comparison against an accepted baseline requests signals; baseline
creation alone does not. KeelMatrix development and repository validation disable telemetry in their own
processes. See the shared privacy policy for current controls and delivery behavior.
