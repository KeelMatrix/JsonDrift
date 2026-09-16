# System.Text.Json compatibility rules

This document defines the structural changes `KeelMatrix.JsonDrift` classifies, the compatibility policies it
offers, and the executed evidence behind every classification. It is the normative rule table for the
package; documentation, diagnostics, and tests must not claim a classification that is not listed here.

## What is classified

JsonDrift compares the *effective* `System.Text.Json` contract of a root type - the metadata the application
actually serializes with - against a stored baseline. Effective metadata is what decides serialized names,
member presence, requiredness, nullability, JSON token kinds, collection shapes, enum representation,
ignored members, captured members, constructor binding, and polymorphism. A C# type change is therefore not
the unit of comparison: the effective serializer contract is.

## Reading protocol

Every classification below was produced by running real `System.Text.Json` serialization and
deserialization. For one contract pair the experiment performs two probes:

* **reader-backward probe** - a value is written with the earlier contract, then read and written back with
  the later contract;
* **writer-forward probe** - a value is written with the later contract, then read and written back with the
  earlier contract.

A probe reports three measured facts: whether deserialization threw, whether every member the earlier
document contained is still observable in what the later contract writes back, and which members or token
shapes were lost. A reading is **lossless** when deserialization succeeds and nothing the earlier document
contained was lost. Members written *in addition* by the later contract are not losses; they are new data.

The JSON comparison is structural: a member is compared by name, JSON token kind, and value. Arrays are
compared element by element, objects member by member, and numbers by value.

### Why reading is defined as lossless reading

A parse-only definition ("did deserialization throw?") would accept almost every structural change, because
`System.Text.Json` silently ignores unrecognized members and silently leaves unmatched members at their
defaults. The measured difference matters:

| Change | parse-only reading | lossless reading |
|---|---|---|
| optional member added | accepted | accepted |
| member removed | accepted, member silently dropped | rejected, member silently dropped |
| member renamed | accepted, value silently becomes the member default | rejected, value silently becomes the member default |
| numeric member becomes a string | rejected | rejected |

Lossless reading is the definition JsonDrift uses, because the durable contracts this package guards -
persisted state, events, files read by another version - lose data under a parse-only definition while still
reporting success.

## Policies

| Policy | Definition used here |
|---|---|
| `ReaderBackward` | A document written by the earlier contract can be read by the later contract without an exception and without losing anything the earlier contract wrote. |
| `WriterForward` | A document written by the later contract can be read by the earlier contract without an exception and without losing anything the later contract wrote. |
| `Full` | Both of the above. |

Each rule below was measured in both directions, so its classification under all three policies is recorded.
Every rule also has an unchanged-contract control case: the same contract reads its own document and must be
reported as compatible, which proves the probe does not report breakage where nothing changed.

## Rule matrix

Proof references name the checks executed by `experiments/KeelMatrix.JsonDrift.RuleMatrix`. `change` is the
probe for the listed change; `control` is the unchanged-contract case.

| Rule | Change | ReaderBackward | WriterForward | Full | Reason and proof |
|---|---|---|---|---|---|
| R01 | An optional member is added to a contract | Compatible | Incompatible | Incompatible | Documents written earlier contain no value for the new member, so the later contract reads them and applies its default. An earlier contract reading a later document discards the new member, which is a loss for that reader. Proof: `R01.property-add.optional`, `.forward`, `.full`, `R01.property-add.optional.control`. |
| R01b | The added member is excluded from the wire contract with `[JsonIgnore]` | Compatible | Compatible | Compatible | The wire contract does not change at all. Proof: `R01.property-add.ignored`, `.forward`, `.full`. |
| R02 | A serialized member is removed from a contract | Incompatible | Compatible | Incompatible | Deserialization succeeds but the removed member is silently dropped, so data written by the earlier contract does not survive a read by the later contract. The earlier contract reads later documents because the later documents simply contain one member less. Proof: `R02.property-removal`, `.forward`, `.full`, `R02.property-removal.control`. |
| R03 | The serialized name of a member is changed (`[JsonPropertyName]`, or a naming-policy change) | Incompatible | Incompatible | Incompatible | Reading succeeds, but the renamed member is unbound: its value silently becomes the member default and the member the earlier document used is dropped. Adding `[JsonExtensionData]` preserves the unbound member as raw data without binding it to the member (`R03.serialized-name.mitigated-by-extension-data`). Proof: `R03.serialized-name.json-property-name`, `R03.serialized-name.naming-policy` and their `.forward`, `.full`, `.control` checks. |
| R04 | An optional member becomes required (`[JsonRequired]` or the `required` modifier) | Incompatible | Compatible | Incompatible | Documents written earlier that omit the member are rejected with `JsonException: ... was missing required properties`. The `required` modifier behaves identically to `[JsonRequired]` (`R04.requiredness.required-keyword`). An explicit `null` satisfies requiredness (`R04.requiredness.explicit-null`). Proof: `R04.requiredness.optional-to-required`, `.forward`, `.full`. |
| R04b | A required member becomes optional | Compatible | Incompatible | Incompatible | Earlier documents always contain the member. Later documents may omit it, and the earlier contract then rejects them. Proof: `R04.requiredness.required-to-optional`, `.forward`, `.full`. |
| R05 | A nullable reference member becomes non-nullable | Compatible | Compatible | Compatible | The metadata difference is always observable (`IsGetNullable`/`IsSetNullable` change from true to false), but with default options `null` is still accepted and re-written unchanged, so nothing is lost. Reading a contract that enforces nullable annotations rejects the document instead (`R05.nullability.reference-nullable-removed.enforced`). Proof: `R05.nullability.metadata`, `R05.nullability.reference-nullable-removed`, `.forward`, `.full`. |
| R05b | A nullable value member becomes non-nullable (`int?` to `int`) | Incompatible | Compatible | Incompatible | `null` can no longer be converted to the member type, so earlier documents that carry an explicit `null` are rejected. Proof: `R05.nullability.value-nullable-removed`, `.forward`, `.full`. |
| R05c | A non-nullable value member becomes nullable (`int` to `int?`) | Compatible | Incompatible | Incompatible | Earlier documents always carry a value. Later documents may carry `null`, which the earlier contract rejects. Proof: `R05.nullability.value-nullable-added`, `.forward`, `.full`. |
| R06 | A numeric member is written as a JSON string, or a string member is read as a number | Incompatible | Incompatible | Incompatible | The token kind is part of the wire contract: numbers are not converted to strings or back, and both directions are rejected. Reading earlier string tokens as numbers while allowing reading from strings parses, but the re-written document changes token kind from string to number, which is still reported (`R06.token-kind.string-to-number.permissive`). Proof: `R06.token-kind.number-to-string`, `.forward`, `.full`. |
| R06b | The numeric width of a member changes (`int` to `long`) | Compatible | Compatible | Compatible | The token kind stays numeric and the value is preserved. Proof: `R06.token-kind.numeric-width`, `.forward`, `.full`. |
| R07 | An array or list member becomes an object member, or an object member becomes a scalar member | Incompatible | Incompatible | Incompatible | The container shape is part of the wire contract and the conversion is rejected in both directions. Proof: `R07.shape.list-to-dictionary`, `R07.shape.dictionary-to-scalar` and their `.forward`, `.full` checks. |
| R07b | A list member becomes an array member (`List<T>` to `T[]`) | Compatible | Compatible | Compatible | Both containers produce the same JSON array. Proof: `R07.shape.list-to-array`, `.forward`, `.full`. |
| R07c | A dictionary key type changes while JSON object keys stay strings | Compatible | Compatible | Compatible | JSON object member names are strings in both contracts and the document is unchanged. Proof: `R07.shape.dictionary-key-type`, `.forward`, `.full`. |
| R08 | An enum member is renamed while the contract uses string tokens | Incompatible | Incompatible | Incompatible | The renamed member is no longer recognized and deserialization is rejected in both directions. Proof: `R08.enum.member-rename`, `.forward`, `.full`. |
| R08b | An enum representation switches between numeric and string tokens | Incompatible | Incompatible | Incompatible | A string-token contract accepts earlier numeric tokens with the default `[JsonStringEnumConverter]` but re-writes them as strings, so the token shape changes (`R08.enum.number-to-string`); with `allowIntegerValues: false` the earlier numeric document is rejected outright (`R08.enum.number-to-string.tokens-only`). A numeric contract reading earlier string tokens is rejected. Proof: `R08.enum.number-to-string`, `.forward`, `.full`. |
| R08c | An enum member is inserted into a numeric representation | Compatible | Compatible | Compatible | The numeric token round-trips unchanged. This is a structural-only classification; see the semantic limitation below. Proof: `R08.enum.member-insertion`, `.forward`, `.full`. |
| R09 | A serialized member becomes ignored (`[JsonIgnore]`), or null members stop being written (`JsonIgnoreCondition`) | Incompatible | Compatible | Incompatible | Ignoring a member removes it from the wire contract exactly like removal, and stopping the write of null members means a member the earlier document contained is no longer present in the later document. Including an ignored member again is compatible in the reading direction, because earlier documents simply do not contain it (`R09.ignore.member-included`). Proof: `R09.ignore.member-excluded`, `R09.ignore.condition-when-writing-null` and their `.forward`, `.full`, `.control` checks. |
| R10 | A polymorphic discriminator value or discriminator property name changes | Incompatible | Incompatible | Incompatible | A renamed discriminator value is reported as an unrecognized discriminator id; a renamed discriminator property leaves the abstract contract without a discriminator and is rejected. Proof: `R10.polymorphism.discriminator-value-renamed`, `R10.polymorphism.discriminator-property-renamed` and their `.forward`, `.full` checks. |
| R10b | An additional derived type is registered | Compatible | Compatible | Compatible | Earlier documents keep resolving because their discriminator values are still registered. Proof: `R10.polymorphism.derived-type-added`, `.forward`, `.full`. |
| R11 | The contract stops capturing members that have no matching property | Incompatible | Compatible | Incompatible | Members the earlier contract captured are dropped when the later contract no longer captures them. Proof: `R11.extension-data.removed`, `.forward`, `.full`. |
| R11b | The contract starts capturing members that have no matching property | Compatible | Incompatible | Incompatible | Earlier documents are read without loss, and unrecognized members are preserved instead of being dropped (`R11.extension-data.present.preserves-unbound-member` versus `R11.extension-data.absent.loses-unbound-member`). An earlier contract reading a later document that carries captured members still drops them. Proof: `R11.extension-data.added`, `.forward`, `.full`. |
| R12 | A constructor parameter is added to a bound constructor | Compatible | Incompatible | Incompatible | By default a missing parameter is bound to its default value, so earlier documents are still read (`R12.binding.constructor-parameter-added`). Reading a contract that rejects missing constructor parameters rejects the same document (`R12.binding.constructor-parameter-added.enforced`), and an earlier contract reading documents written with the added member drops it. Proof: `R12.binding.constructor-parameter-added`, `.forward`, `.full`. |
| R12b | An added constructor parameter has a default value | Compatible | Incompatible | Incompatible | Earlier documents are read with the default applied; documents written by the later contract carry the member the earlier contract drops. Proof: `R12.binding.constructor-parameter-defaulted`, `.forward`, `.full`. |
| R12c | A constructor parameter that binds by name is renamed | Incompatible | Incompatible | Incompatible | The renamed parameter no longer binds, so the earlier member is dropped and the value silently becomes the parameter default. Proof: `R12.binding.constructor-parameter-renamed`, `.forward`, `.full`. |
| R13 | The source-generated context declares different contract options than the previous contract | Incompatible | Compatible | Incompatible | Options declared on `JsonSerializerContext` are part of the effective contract: a context that stops writing null members produces documents that no longer contain them. Proof: `R13.source-generation.context-options`, `.forward`, `.full`. |
| R13b | The same contract is described from reflection metadata and from a source-generated context | Equivalent | Equivalent | Equivalent | With the same registered types, both metadata sources produce byte-identical canonical contract documents (`R13.source-generation.metadata-parity`), and a required-member change is classified identically by both (`R13.source-generation.classification-parity`). A type the context does not register has no metadata at all and is reported as unavailable (`R13.source-generation.unregistered-type`). |

## Unsupported and unclassified metadata

An unrecognized converter or metadata source prevents sound classification. These cases are reported as
unsupported, are never mapped to compatible, and fail an assertion that requires a compatible result. The
detection is based on declared metadata only; JsonDrift never executes a converter to discover its behavior.

| Rule | Case | Classification | Reason and proof |
|---|---|---|---|
| R14 | A member declares a custom converter | Unsupported | The declared converter type is not part of `System.Text.Json`. Proof: `R14.converter.opaque-property`. |
| R14b | The root type declares a custom converter | Unsupported | Same, for the whole contract. Proof: `R14.converter.opaque-root`. |
| R14c | A custom converter is registered on the serializer options | Unsupported | The converter applies to the member type without a member-level declaration. Proof: `R14.converter.opaque-from-options`. |
| R14d | A custom metadata resolver supplies the contract | Unsupported | A custom resolver can replace a member converter without leaving a declared marker, so its metadata cannot be classified. Metadata from the default reflection resolver and from source-generated contexts is accepted. Proof: `R14.converter.custom-metadata-resolver`, `R14.converter.default-resolver-supported`, `R14.converter.source-generation-resolver-supported`. |
| R14e | A framework converter the library knows is applied | Supported | `JsonStringEnumConverter` is classified as string enum tokens rather than as opaque. Proof: `R14.converter.known-converter-supported`. |
| U02 | A run contains compatible changes and one unclassifiable contract | Unsupported | A lossless round trip is not sufficient: a custom converter can round-trip a document and still be unclassifiable, so the result stays unsupported and the assertion fails. Proof: `R14.converter.lossless-round-trip.still-unsupported`, `U02.unsupported.never-green`. |

## Canonical contract document

The canonical document describes the effective contract in a stable form: members sorted by name, a fixed key
order, `contractVersion` for future format revisions, nested shapes described by reference so recursive
contracts terminate, LF line endings, UTF-8 without a byte order mark, and no timestamps, host paths, or
process-specific values.

| Rule | Property | Evidence |
|---|---|---|
| D01 | Repeated canonicalization of the same contract is byte-identical | Three independently created option sets produce the same document (`D01.canonical-document.repeatable`); cross-process equality is checked by `scripts/validate-rule-matrix.ps1`. |
| D01b | The document is LF-terminated and contains no carriage returns | `D01.canonical-document.line-endings`. |
| D01c | The document contains no host paths | `D01.canonical-document.host-independent`. |
| D02 | Equivalent option sets produce identical documents regardless of converter registration order | `D02.canonical-document.options-equivalent`. |
| D03 | A contract that refers to its own type canonicalizes deterministically | `D03.canonical-document.recursive-type`. |

## Documented limitations

1. **Semantic compatibility is out of scope.** Inserting an enum member into a numeric representation is
   wire-compatible and round-trips unchanged, but the value that used to denote one member may now denote
   another (`R08.enum.member-insertion`). JsonDrift reports structural compatibility and cannot detect a
   change of meaning, a changed unit, or a changed business rule.
2. **Reference nullability alone does not change reading.** A nullable-to-non-nullable reference member is a
   metadata change, not a reading failure, unless the reader enables nullable annotation enforcement, in
   which case the same document is rejected. Both measurements are recorded above.
3. **Converter detection is declaration-based.** Converters are classified from the converter attribute on the
   member, on the member type, or on the root type, from converters registered on the options, and from the
   identity of the metadata resolver. A contract whose metadata comes from an unrecognized resolver is
   reported as unsupported rather than inspected.
4. **Only measured behavior is claimed.** A serializer feature that is not listed in this document has no
   classification and must be reported as unsupported or unclassified until it is measured.
5. **Baseline storage, limits, and version migration are not covered here.** This document defines
   classification semantics only.

## Reproducing the evidence

```pwsh
dotnet restore KeelMatrix.JsonDrift.sln --configfile NuGet.config
dotnet build KeelMatrix.JsonDrift.sln -c Release --no-restore
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release --no-build -- --matrix --verbose
```

The experiment prints every check with its expected classification and the measured result, and exits with a
non-zero code when any measurement disagrees with the recorded classification. The measurements recorded here
were produced on .NET 8.0.31 with `System.Text.Json` 10.0.12 (assembly version 10.0.0.0) on Windows 10
(10.0.19045), using .NET SDK 8.0.425.
