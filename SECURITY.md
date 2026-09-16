# Security

Please report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/KeelMatrix/JsonDrift/security/advisories/new). Do not include
secrets or sensitive contract documents in a public issue.

The supported version is the latest released version of `KeelMatrix.JsonDrift`. Development builds are not
supported release targets.

Contract documents and baselines are treated as source-code-level information: they contain property names,
structural type information, enum labels, and discriminator values that describe an application's internals.
JsonDrift reads local serializer metadata and local baseline files only. It never uploads contract content,
baseline content, file paths, or repository identity, and it never executes application custom converters in
order to discover their behavior. A serializer feature that cannot be classified from trustworthy metadata is
reported as unsupported instead of being assumed compatible.

Baseline files are untrusted input. Parsing is bounded, malformed or future-version documents are rejected
explicitly, and recursive contract graphs are traversed with cycle protection.
