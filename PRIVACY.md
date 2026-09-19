# Privacy

JsonDrift reads `System.Text.Json` metadata and local canonical baselines. Extraction, baseline creation,
comparison, and assertion are local operations and do not require network access.

## What is collected

After a real comparison of at least one root contract against an accepted baseline, JsonDrift requests a
best-effort activation signal and the shared client's low-frequency weekly heartbeat. Baseline creation alone
is not an activation. JsonDrift sends no product-specific comparison payload.

The shared client emits these standard fields for JsonDrift:

- Activation: `event=activation`, the `jsondrift` tool identifier, package version, telemetry version,
  schema version, pseudonymous project and installation fingerprints, runtime, operating system, CI marker,
  and timestamp.
- Heartbeat: `event=heartbeat`, the `jsondrift` tool identifier, package version, telemetry version, schema
  version, pseudonymous project and installation fingerprints, and ISO week.

The project fingerprint is `SHA-256("repo.v1" + normalized repository key)`. The normalized repository key is
derived, in order of preference, from the CI repository identity (for example `GITHUB_SERVER_URL` plus
`GITHUB_REPOSITORY`), the local git `origin` remote URL, the git root commit using
`SHA-256("git-root.v1" + root commit hash)`, or project-file content as a final fallback. This is a stable,
unsalted pseudonym of the consuming codebase, deliberately used to correlate the same project across machines
and time; the per-installation machine salt applies only to the separate `installation_hash`. Ordinary analytics
cannot reverse these fingerprints, and no raw identity is transmitted. They are used only to distinguish
projects/installations and to count activation and repeat use. The shared package is the source of truth for
delivery behavior, retention, runtime metadata, repository-local configuration, and process opt-out precedence. See the
[KeelMatrix.Telemetry privacy documentation](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md)
for those infrastructure details.

## What is not sent

Contract and baseline documents never leave the machine. JsonDrift never transmits a raw repository URL, path,
commit identifier, type or member name, enum label, discriminator value, converter name, serializer configuration
value, project-file content, contract content, baseline content, or arbitrary exception message. The pseudonymous
fingerprints above are derived locally and are not raw repository identity. Local diagnostics may contain
developer-facing contract detail because they stay on the machine.

## Controls and failure isolation

Disable telemetry for a process with any of these truthy values:

```text
KEELMATRIX_NO_TELEMETRY=1
DOTNET_CLI_TELEMETRY_OPTOUT=1
DO_NOT_TRACK=1
```

The shared client also resolves its repository-local telemetry configuration before delivery. KeelMatrix
development and repository validation set the opt-out variables explicitly. Telemetry is best-effort and is
handed to one bounded, non-awaited background dispatch with at most one pending emission; a slow or blocking
client cannot delay the comparison caller, although a saturated queue may drop a signal. Telemetry must never
throw into or alter extraction, comparison, report classification, or assertions. If the client, queue,
configuration, or delivery path fails, JsonDrift continues with the original result. The documented caller-path
bound is 250 ms on the Windows validation host: the regression suite compares the same operation with a no-op
client and an injected client that blocks indefinitely, and requires both caller paths to stay below that bound.
