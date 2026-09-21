# Privacy

JsonDrift reads `System.Text.Json` metadata and local canonical baselines. Extraction, baseline creation,
comparison, and assertion are local operations and do not require network access.

## What is collected

After a real comparison of at least one root contract against an accepted baseline, JsonDrift requests
best-effort activation and heartbeat signals. Baseline creation alone is not an activation. JsonDrift sends no
product-specific comparison payload.

The shared `KeelMatrix.Telemetry` package is the source of truth for event fields, pseudonymous identifiers,
delivery behavior, retention, runtime metadata, repository-local configuration, and process opt-out precedence.
See the [KeelMatrix.Telemetry privacy documentation](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md)
for those shared implementation details.

## What is not sent

Contract and baseline documents never leave the machine. JsonDrift never transmits a raw repository URL, path,
commit identifier, type or member name, enum label, discriminator value, converter name, serializer configuration
value, project-file content, contract content, baseline content, or arbitrary exception message. Local diagnostics
may contain developer-facing contract detail because they stay on the machine.

## Controls and failure isolation

Disable telemetry for a process with any of these truthy values:

```text
KEELMATRIX_NO_TELEMETRY=1
DOTNET_CLI_TELEMETRY_OPTOUT=1
DO_NOT_TRACK=1
```

KeelMatrix development and repository validation set the opt-out variables explicitly. Telemetry is best-effort;
failure cannot change extraction, comparison, report classification, or assertions. Delivery behavior and the
shared opt-out precedence remain defined by the shared telemetry policy.
