# Privacy

JsonDrift reads `System.Text.Json` metadata and local canonical baselines. Extraction, baseline creation,
comparison, and assertion are local operations and do not require network access.

## What is collected

After a real comparison of at least one root contract against an accepted baseline, JsonDrift requests a
best-effort activation signal and the shared client's low-frequency weekly heartbeat. Baseline creation alone
is not an activation. The product-specific comparison summary is limited to:

- package version;
- target framework;
- compatibility mode;
- a coarse root-contract count;
- pass, incompatible, or unsupported outcome; and
- whether source-generated metadata was used.

JsonDrift uses `KeelMatrix.Telemetry` for delivery and its pseudonymous project/install identity. The shared
package is the source of truth for that identity, delivery behavior, retention, standard runtime metadata,
repository-local configuration, and process opt-out precedence. See the
[KeelMatrix.Telemetry privacy documentation](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md)
for those infrastructure details.

## What is not sent

Contract and baseline documents never leave the machine. JsonDrift does not send namespaces, type or property
names, paths, converter names, enum values, example JSON, discriminator values, serializer configuration
values, repository identity, or arbitrary exception messages. Local diagnostics may contain developer-facing
contract detail because they stay on the machine.

## Controls and failure isolation

Disable telemetry for a process with any of these truthy values:

```text
KEELMATRIX_NO_TELEMETRY=1
DOTNET_CLI_TELEMETRY_OPTOUT=1
DO_NOT_TRACK=1
```

The shared client also resolves its repository-local telemetry configuration before delivery. KeelMatrix
development and repository validation set the opt-out variables explicitly. Telemetry is best-effort and
must never block, throw into, or alter extraction, comparison, report classification, or assertions. If the
client, queue, configuration, or delivery path fails, JsonDrift continues with the original result.
