# Privacy

JsonDrift compares a contract document derived from local `System.Text.Json` metadata against a local baseline
file. Contract extraction and comparison are completely offline and require no account, service, or network
access.

This repository does not collect or transmit usage data. The compatibility-rule experiment in
`experiments/KeelMatrix.JsonDrift.RuleMatrix` reads only the types and serializer options compiled into it and
writes canonical documents to the output directory it is given.

Contract documents and baselines contain property names, structural type information, enum labels, and
discriminator values. They are source-code-level information and must be treated as such: they are never
included in diagnostics that leave the machine, and any future telemetry will never carry contract or baseline
content, file paths, or repository identity.

Any future telemetry, if introduced, will remain minimal, documented before use, and separable from local
development and validation activity.
