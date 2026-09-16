## Navigation

- `experiments/KeelMatrix.JsonDrift.RuleMatrix` is the executable compatibility-rule matrix. It contains the
  contract fixtures (`Contracts/`), the measurement harness (`Matrix/`), and one file per rule family
  (`Rules/`).
- `docs/compatibility-rules.md` is the normative rule table: rule identifier, change, classification under
  each policy, reason, and the check that proves it.
- `docs/initial-release-scope.md` records the recommended first-release scope and its evidence.
- `scripts/validate-rule-matrix.ps1` is the repository-controlled validation entry point.

No packable project exists yet. The public API, baseline format, and package are deliberately absent.

## Commands

```text
dotnet restore KeelMatrix.JsonDrift.sln --configfile NuGet.config
dotnet build KeelMatrix.JsonDrift.sln -c Release --no-restore
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release --no-build -- --matrix --verbose
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release --no-build -- --canonical artifacts/canonical
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
- The experiment project is not packable and must stay that way until the product scope is implemented.

## Validation

Run the focused rule matrix first, then the Release solution build and the full
`scripts/validate-rule-matrix.ps1` gate. Re-run the matrix whenever a rule, a fixture, or the
`System.Text.Json` package version changes, and update `docs/compatibility-rules.md` from the measured output
rather than from expectation.

Repository validation is local by design; no automated workflow is configured here.
