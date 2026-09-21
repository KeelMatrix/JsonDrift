## Navigation

- `src/KeelMatrix.JsonDrift` is the packable `net8.0` library and owns the promoted traversal, deny-by-default
  classification, and canonical baseline model.
- `tests/KeelMatrix.JsonDrift.Tests` contains the foundation API, determinism, parsing, concurrency, and XML-doc
  contract tests.
- `experiments/KeelMatrix.JsonDrift.RuleMatrix` is the executable compatibility-rule matrix. It contains the
  contract fixtures (`Contracts/`), the measurement harness (`Matrix/`), and one file per rule family
  (`Rules/`); its promoted mechanism comes from the product project reference.
- `docs/compatibility-rules.md` is the normative rule table: rule identifier, change, classification under
  each policy, reason, and the check that proves it.
- `docs/initial-release-scope.md` records the recommended first-release scope and its evidence.
- `scripts/validate.ps1` is the repository-controlled local CI-equivalent validation entry point.
- `scripts/validation-manifest.json` is the fail-closed required-check manifest.


## Commands

```text
dotnet restore KeelMatrix.JsonDrift.sln --configfile NuGet.config
dotnet build KeelMatrix.JsonDrift.sln -c Release --no-restore
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release --no-build -- --matrix --verbose
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release --no-build -- --canonical artifacts/canonical
dotnet test KeelMatrix.JsonDrift.sln -c Release --no-build
pwsh scripts/validate.ps1
```

## Invariants

- A classification is only valid when it is produced by executed `System.Text.Json` serialization and
  deserialization, not by inspection. Every rule needs a change case and an unchanged-contract control case.
- A check declares the expected classification and fails when the measurement disagrees. Changing a rule
  means changing both the expectation and `docs/compatibility-rules.md`.
- ReaderBackward means lossless reading: deserialization must succeed and nothing the earlier document
  contained may be lost. Silent data loss is a break.
- Unsupported or unrecognized converter metadata never maps to compatible, even when a round trip happens to
  be lossless.
- Classification is deny by default: a contract is supported only when one recursive traversal recorded its
  shape evidence and its converter and resolver metadata matches the allowlists in
  `docs/compatibility-rules.md`. There is one traversal and one recorded model; the canonical document and the
  classification both read it, and the coverage gate derives the implemented discovery sources from the walk
  rather than from a hand-maintained list.
- Canonical contract documents are sorted by member name, LF-terminated, UTF-8 without a byte order mark, and
  free of timestamps, host paths, and process-specific values.
- The experiment project is not packable and must stay that way.
- The product is `net8.0` only and exposes `ReaderBackward` only in this release slice.
- Reads never create, update, or rewrite baselines; baseline mutation requires an explicit create/update call.

## Validation

Run the focused rule matrix first, then the Release solution build and the full
`scripts/validate.ps1` gate. Re-run the matrix whenever a rule, a fixture, or the
`System.Text.Json` package version changes, and update `docs/compatibility-rules.md` from the measured output
rather than from expectation.

The same `scripts/validate.ps1` gate runs in `.github/workflows/ci.yml` on GitHub-hosted `windows-latest`,
`ubuntu-latest`, and `macos-latest` for pushes and pull requests to `main`. The script remains the local source
of truth and includes the required direct-and-transitive vulnerability audit. The repository claims Windows,
Linux, and macOS support for the pinned `net8.0` SDK/runtime combination; other runtime/SDK combinations remain
unverified and must not be implied by developer documentation.
