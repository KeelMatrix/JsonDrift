# Initial release scope

This document records the scope decisions for the first release of `KeelMatrix.JsonDrift` and the measured
evidence behind each recommendation. The rule definitions themselves are in
[docs/compatibility-rules.md](compatibility-rules.md); every claim below refers to a check that is executed by
`experiments/KeelMatrix.JsonDrift.RuleMatrix`.

## 1. Compatibility policies

**Recommendation: the first release ships `ReaderBackward` only.** Forward and full modes stay out of the
shipped surface until their reading semantics are decided and proven on their own.

Evidence:

* Under lossless reading, forward compatibility rejects ordinary additive evolution. Measured
  `WriterForward=Incompatible` (and therefore `Full=Incompatible`) for adding an optional member
  (`R01.property-add.optional`), adding captured members (`R11.extension-data.added`), and adding a bound
  constructor parameter (`R12.binding.constructor-parameter-added`). An earlier contract reading a later
  document silently drops the member the later contract introduced.
* Only a small set of measured changes is compatible in both directions. The set is read from the matrix output
  and its size is asserted by `D04.policy.full-compatible-count`, so the number in this document cannot drift
  from the executed evidence: adding a member that is excluded from the wire contract
  (`R01.property-add.ignored`), a nullable reference member becoming non-nullable with default options
  (`R05.nullability.reference-nullable-removed`), an `int` member widening to `long`
  (`R06.token-kind.numeric-widening`), `List<T>` becoming `T[]` (`R07.shape.list-to-array`), inserting an enum
  member into a numeric representation (`R08.enum.member-insertion`), and registering an additional derived
  type (`R10.polymorphism.derived-type-added`). Every other measured change is incompatible under at least one
  policy. The unsupported set is likewise read from the matrix output
  (`D04.policy.unsupported-count` reports the 47 checks that record unsupported metadata).
* Two measured changes are deliberately not classified in either direction. A dictionary key-type change is
  reported unsupported (`R07.shape.dictionary-key-type`) because compatibility depends on the earlier key
  value space, which the contract model does not record: the key `"1"` is read back unchanged by an
  `int`-keyed contract while the key `"abc"` is rejected. A narrowing numeric change is classified
  incompatible on its own evidence (`R06.token-kind.numeric-narrowing`) rather than sharing the widening
  rule's classification.
* A forward or full policy therefore forces a semantic decision that `ReaderBackward` does not: either the
  lossless definition is kept and most evolution is rejected, or a parse-only definition is introduced, under
  which a rename or removal is accepted in the reverse direction while the value is silently dropped. The
  parse-only variant has its own rule-by-rule proof obligation and is not part of the measured matrix.
* `ReaderBackward` is the only policy whose semantics are proven for every shipped rule in both a change case
  and an unchanged-contract control case: the matrix runs 207 executed checks and all of them agree with the
  recorded classification. The count is asserted by `D04.matrix.check-count`, so adding, removing, or
  reclassifying a check fails the matrix until this document is updated with it.
* The invariant that unsupported or opaque metadata never maps to compatible is enforced by construction
  rather than by a list of known cases. Classification is deny by default: a contract is only supported when
  a single recursive traversal recorded its shape evidence and its converter and resolver metadata matches an
  explicit framework allowlist, and everything else is reported unsupported. Every metadata-resolution path
  the walk visits is inventoried in the path inventory of
  [docs/compatibility-rules.md](compatibility-rules.md), covered by an executed check, and bounded by a shared
  visited-type set plus a traversal budget; the matrix compares the sources the traversal actually visited
  with the inventory and the checks (`D06.discovery-paths.inventory`), binds the classification rules and the
  allowlists to the documented lists (`D06.classification-rules.documented`,
  `D06.allowlist.documented`), and fails when a discovery source, a recorded node kind, or a rule is added
  without coverage. The canonical document carries an aggregate support state as well as a root flag
  (`R15.canonical-document.aggregate-support-state`), so a report layer never infers safety from a root flag
  while nested metadata is unsupported. An executable adversarial set (`A01.adversarial.*`) keeps trying to
  hide opaque metadata behind a path no rule names, and every case has to fail closed.

Consequence for the API: one policy is exposed for the first release, so there is no second, weaker definition
of "compatible" for users to misinterpret. The forward and full measurements already exist in the experiment,
so a later decision to ship them starts from measured behavior rather than from a new investigation.

## 2. Target frameworks

**Recommendation: the first release targets `net8.0` only.**

Evidence:

* Every measurement in the rule matrix was produced on `net8.0` with the `System.Text.Json` 10.0.12 package
  (assembly version 10.0.0.0) running on .NET 8.0.31. No other target framework has been measured, so no
  other target framework currently has a proven classification table.
* The metadata surface the rules depend on is available to `net8.0` through that package: `JsonTypeInfo.Kind`
  and `ElementType`, `JsonPropertyInfo.IsRequired`, `IsGetNullable`, `IsSetNullable`, and `IsExtensionData`,
  `JsonTypeInfo.PolymorphismOptions`, `JsonSerializerOptions.RespectNullableAnnotations`, and
  `JsonSerializerOptions.RespectRequiredConstructorParameters`.
* Two rules are sensitive to reader options that exist in that modern package surface
  (`R05.nullability.reference-nullable-removed.enforced`,
  `R12.binding.constructor-parameter-added.enforced`). A target that cannot offer that surface would change
  the classification for those rules, so it would need its own measurement rather than an assumption.
* Targeting `netstandard2.0` would push a modern `System.Text.Json` package graph onto consumers of a
  test-time tool. The metadata needed for the shipped rules is not available there without that change.

Consequence: adding a target framework later is a measurement task, not a packaging task. The rule matrix has
to be re-run for that target before the package claims support for it.

## 3. Microsoft JSON-schema exporter

**Recommendation: keep the JSON-schema exporter out of the first release.**

Evidence:

* The exporter is available in the same package version the rule matrix uses:
  `System.Text.Json.Schema.JsonSchemaExporter` exists in the `net8.0` asset of `System.Text.Json` 10.0.12, and
  `GetJsonSchemaAsNode` produces a general JSON Schema from the same `JsonTypeInfo` metadata. It would not add
  a package dependency, but it would add a second, version-coupled representation of the contract.
* The exporter describes one contract; it does not classify the difference between two contracts. Every
  classification in the rule matrix - requiredness, nullability flags, token kind, container shape, ignore
  behavior, captured members, constructor binding, polymorphism, and unsupported detection - was derived from
  `JsonTypeInfo` metadata plus executed `System.Text.Json` behavior. No measured rule needed schema output.
* General JSON Schema generation is also a different job: it describes declared shape, while JsonDrift answers
  whether an evolution can still be read. Adopting it as a source of truth for classification would weaken
  the unsupported-metadata posture, because a schema does not distinguish a classifiable contract from an
  opaque converter.

Consequence: if the exporter is adopted later, it may only be an optional cross-check that cannot promote an
unsupported contract to compatible, and the rule matrix has to be re-run against that adoption.
