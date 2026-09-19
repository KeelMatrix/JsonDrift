# Security Policy

## Reporting a Vulnerability

Please report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/KeelMatrix/JsonDrift/security/advisories/new). Do not disclose
vulnerabilities through a public issue. Include the affected package version, a concise reproduction, the
security impact, the runtime and operating system, and only sanitized contract or baseline details; do not
include secrets or sensitive contract documents.

Ordinary defects that do not involve a security vulnerability may be reported through the repository issue
tracker. This policy is for vulnerability disclosure and is separate from ordinary bug and community-conduct
reporting.

## Supported Versions

The latest released version of `KeelMatrix.JsonDrift` is supported. Development builds are not supported release
targets.

## Security Considerations

Contract documents and baselines are treated as source-code-level information: they contain property names,
structural type information, enum labels, and discriminator values that describe an application's internals.
JsonDrift reads local serializer metadata and local baseline files only. It never uploads contract content,
baseline content, raw repository URLs, absolute paths, commit identifiers, or other raw identity strings, and
it never executes application custom converters in order to discover their behavior. A serializer feature that
cannot be classified from trustworthy metadata is reported as unsupported instead of being assumed compatible.

Baseline files are untrusted input. Parsing is bounded, malformed or future-version documents are rejected
explicitly, and recursive contract graphs are traversed with cycle protection.
