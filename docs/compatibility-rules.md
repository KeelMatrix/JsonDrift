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
compared element by element, objects member by member, and numbers by **exact value**
(`C01.document-comparison.numbers-by-value`). Exact value means that both written numbers are reduced to
their sign, their significant digits, and the decimal exponent of their last significant digit, and are
equal only when those agree: `5` and `5.0`, and `1e2` and `100`, carry the same value, while
`10000000000000000000000000000001` and `10000000000000000000000000000000`,
`123456789012345678901234567890.1` and `123456789012345678901234567890.2`,
`0.100000000000000000000000000001` and `0.1`, `1E+400` and `1E+401`, and `1E-400` and `0` do not
(`C01.document-comparison.exact-value`, `C01.document-comparison.non-finite-numbers`). The comparison never
goes through `decimal` or `double`, so a rounded or non-finite parse cannot hide a magnitude difference, and
no value is compared by serialized text alone.

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
| R02 | A serialized member is removed from a contract | Incompatible | Compatible | Incompatible | Deserialization succeeds but the removed member is silently dropped, so data written by the earlier contract does not survive a read by the later contract. The earlier contract reads later documents because the later documents simply contain one member less. When the removed member was bound by a constructor parameter, the change reason reports that binding context while remaining classified by R02. Proof: `R02.property-removal`, `.forward`, `.full`, `R02.property-removal.control`, `R02.property-removal.constructor-bound`, `.forward`, `.full`, `.control`. |
| R03 | The serialized name of a member is changed (`[JsonPropertyName]`, or a naming-policy change) | Incompatible | Incompatible | Incompatible | Reading succeeds, but the renamed member is unbound: its value silently becomes the member default and the member the earlier document used is dropped. Adding `[JsonExtensionData]` preserves the unbound member as raw data without binding it to the member (`R03.serialized-name.mitigated-by-extension-data`). Proof: `R03.serialized-name.json-property-name`, `R03.serialized-name.naming-policy` and their `.forward`, `.full`, `.control` checks. |
| R04 | An optional member becomes required (`[JsonRequired]` or the `required` modifier) | Incompatible | Compatible | Incompatible | Documents written earlier that omit the member are rejected with `JsonException: ... was missing required properties`. The `required` modifier behaves identically to `[JsonRequired]` (`R04.requiredness.required-keyword`). An explicit `null` satisfies requiredness (`R04.requiredness.explicit-null`). Proof: `R04.requiredness.optional-to-required`, `.forward`, `.full`. |
| R04b | A required member becomes optional | Compatible | Incompatible | Incompatible | Earlier documents always contain the member. Later documents may omit it, and the earlier contract then rejects them. Proof: `R04.requiredness.required-to-optional`, `.forward`, `.full`. |
| R05 | A nullable reference member becomes non-nullable | Compatible | Compatible | Compatible | The metadata difference is always observable (`IsGetNullable`/`IsSetNullable` change from true to false), but with default options `null` is still accepted and re-written unchanged, so nothing is lost. Reading a contract that enforces nullable annotations rejects the document instead (`R05.nullability.reference-nullable-removed.enforced`). Proof: `R05.nullability.metadata`, `R05.nullability.reference-nullable-removed`, `.forward`, `.full`. |
| R05b | A nullable value member becomes non-nullable (`int?` to `int`) | Incompatible | Compatible | Incompatible | `null` can no longer be converted to the member type, so earlier documents that carry an explicit `null` are rejected. Proof: `R05.nullability.value-nullable-removed`, `.forward`, `.full`. |
| R05c | A non-nullable value member becomes nullable (`int` to `int?`) | Compatible | Incompatible | Incompatible | Earlier documents always carry a value. Later documents may carry `null`, which the earlier contract rejects. Proof: `R05.nullability.value-nullable-added`, `.forward`, `.full`. |
| R06 | A numeric member is written as a JSON string, or a string member is read as a number | Incompatible | Incompatible | Incompatible | The token kind is part of the wire contract: numbers are not converted to strings or back, and both directions are rejected. Reading earlier string tokens as numbers while allowing reading from strings parses, but the re-written document changes token kind from string to number, which is still reported (`R06.token-kind.string-to-number.permissive`). Proof: `R06.token-kind.number-to-string`, `.forward`, `.full`. |
| R06b | A member widens from `int` to `long` | Compatible | Compatible | Compatible | The token kind stays numeric and every `int` value is representable as `long`. The probes use the boundary values `int.MaxValue` and `int.MinValue`. Proof: `R06.token-kind.numeric-widening`, `.forward`, `.full`. |
| R06c | A member narrows from `long` to `int` | Incompatible | Compatible | Incompatible | The token kind stays numeric, but a value outside the target range can no longer be converted: a `long` document carrying `3000000000` read by an `int` member is rejected with `JsonException: The JSON value could not be converted to System.Int32`. The recorded probe therefore uses a value beyond the narrower type's range. Proof: `R06.token-kind.numeric-narrowing`, `.forward`, `.full`. |
| R07 | An array or list member becomes an object member, or an object member becomes a scalar member | Incompatible | Incompatible | Incompatible | The container shape is part of the wire contract and the conversion is rejected in both directions. Proof: `R07.shape.list-to-dictionary`, `R07.shape.dictionary-to-scalar` and their `.forward`, `.full` checks. |
| R07b | A list member becomes an array member (`List<T>` to `T[]`) | Compatible | Compatible | Compatible | Both containers produce the same JSON array. Proof: `R07.shape.list-to-array`, `.forward`, `.full`. |
| R07c | A dictionary key type changes while JSON object keys stay strings | Unsupported | Unsupported | Unsupported | Reader compatibility depends on whether every key the earlier contract could write is representable in the later key type, which the contract model does not record. Both directions are executed: the key `"1"` written by an earlier `Dictionary<string, int>` is read back unchanged by a later `Dictionary<int, int>` (`R07.shape.dictionary-key-type.representable`), while the key `"abc"` is rejected with `JsonException: The JSON value could not be converted to System.Int32` (`R07.shape.dictionary-key-type.unrepresentable`). One key value space is therefore insufficient evidence with missing keys, and key type identity alone is insufficient evidence without it, so the change is reported unsupported. Proof: `R07.shape.dictionary-key-type`. |
| R08 | An enum member is renamed while the contract uses string tokens | Incompatible | Incompatible | Incompatible | The renamed member is no longer recognized and deserialization is rejected in both directions. Proof: `R08.enum.member-rename`, `.forward`, `.full`. |
| R08b | An enum representation switches between numeric and string tokens | Incompatible | Incompatible | Incompatible | A string-token contract accepts earlier numeric tokens with the default `[JsonStringEnumConverter]` but re-writes them as strings, so the token shape changes (`R08.enum.number-to-string`); with `allowIntegerValues: false` the earlier numeric document is rejected outright (`R08.enum.number-to-string.tokens-only`). A numeric contract reading earlier string tokens is rejected. Proof: `R08.enum.number-to-string`, `.forward`, `.full`. |
| R08c | An enum member is inserted into a numeric representation | Compatible | Compatible | Compatible | The numeric token round-trips unchanged. This is a structural-only classification; see the semantic limitation below. Proof: `R08.enum.member-insertion`, `.forward`, `.full`. |
| R08d | The naming policy applied to string enum members changes | Incompatible | Incompatible | Incompatible | The members are still written as strings, but their serialized names change: a document written with an `inProgress` policy cannot be read by a contract whose converter writes `in_progress`, and deserialization is rejected in both directions, because the earlier name is not recognized as an enum member. Proof: `R08.enum.naming-policy`, `R08.enum.naming-policy.document`, `.forward`, `.full`. |
| R09 | A serialized member becomes ignored (`[JsonIgnore]`), or null members stop being written (`JsonIgnoreCondition`) | Incompatible | Compatible | Incompatible | Ignoring a member removes it from the wire contract exactly like removal, and stopping the write of null members means a member the earlier document contained is no longer present in the later document. Including an ignored member again is compatible in the reading direction, because earlier documents simply do not contain it (`R09.ignore.member-included`). Proof: `R09.ignore.member-excluded`, `R09.ignore.condition-when-writing-null` and their `.forward`, `.full`, `.control` checks. |
| R10 | A polymorphic discriminator value or discriminator property name changes | Incompatible | Incompatible | Incompatible | A renamed discriminator value is reported as an unrecognized discriminator id; a renamed discriminator property leaves the abstract contract without a discriminator and is rejected. Proof: `R10.polymorphism.discriminator-value-renamed`, `R10.polymorphism.discriminator-property-renamed` and their `.forward`, `.full` checks. |
| R10b | An additional derived type is registered | Compatible | Compatible | Compatible | Earlier documents keep resolving because their discriminator values are still registered. Proof: `R10.polymorphism.derived-type-added`, `.forward`, `.full`. |
| R11 | The contract stops capturing members that have no matching property | Incompatible | Compatible | Incompatible | Members the earlier contract captured are dropped when the later contract no longer captures them. Proof: `R11.extension-data.removed`, `.forward`, `.full`. |
| R11b | The contract starts capturing members that have no matching property | Compatible | Incompatible | Incompatible | Earlier documents are read without loss, and unrecognized members are preserved instead of being dropped (`R11.extension-data.present.preserves-unbound-member` versus `R11.extension-data.absent.loses-unbound-member`). An earlier contract reading a later document that carries captured members still drops them. Proof: `R11.extension-data.added`, `.forward`, `.full`. |
| R12 | A constructor parameter is added to a bound constructor | Compatible | Incompatible | Incompatible | By default a missing parameter is bound to its default value, so earlier documents are still read (`R12.binding.constructor-parameter-added`). Reading a contract that rejects missing constructor parameters rejects the same document (`R12.binding.constructor-parameter-added.enforced`), and an earlier contract reading documents written with the added member drops it. Proof: `R12.binding.constructor-parameter-added`, `.forward`, `.full`. |
| R12b | An added constructor parameter has a default value | Compatible | Incompatible | Incompatible | Earlier documents are read with the default applied; documents written by the later contract carry the member the earlier contract drops. Proof: `R12.binding.constructor-parameter-defaulted`, `.forward`, `.full`. |
| R12c | A constructor parameter that binds by name is renamed | Incompatible | Incompatible | Incompatible | The renamed parameter no longer binds, so the earlier member is dropped and the value silently becomes the parameter default. Proof: `R12.binding.constructor-parameter-renamed`, `.forward`, `.full`. |
| R13 | The source-generated context declares different contract options than the previous contract | Incompatible | Compatible | Incompatible | Options declared on `JsonSerializerContext` are part of the effective contract: a context that stops writing null members produces documents that no longer contain them. Proof: `R13.source-generation.context-options`, `.forward`, `.full`. |
| R13b | The same contract is described from reflection metadata and from a source-generated context | Equivalent | Equivalent | Equivalent | With the same registered types and option sets whose recorded values are equivalent, both metadata sources produce byte-identical canonical contract documents (`R13.source-generation.metadata-parity`), and a required-member change is classified identically by both (`R13.source-generation.classification-parity`). A pair whose recorded option values differ is not compared as an equivalent pair: a context that declares `DefaultIgnoreCondition = WhenWritingNull` stops writing null members, which is measured as a reading change (`R13.source-generation.context-options`) and as an option difference in the document. A type the context does not register has no metadata at all and is reported as unavailable (`R13.source-generation.unregistered-type`). |

The R12/R12b/R12c family covers only added, defaulted, and renamed constructor parameters. Removing a serialized member is classified by R02 even when that member was bound by a constructor parameter; it is not a separate constructor-binding removal rule.

### Shipping comparison rules for nested contracts and scalar roots

The shipping `JsonDrift.Compare` and `JsonDriftReport.AssertCompatible` APIs use the `ReaderBackward`
classification below. A baseline records the complete reachable contract graph: nested object members,
collection elements, and dictionary keys and values carry their own node metadata. Repeated types are written
as bounded references, and missing graph evidence is `Unsupported`; a CLR type-name difference is never used
as a substitute for comparing the child contract.

Root scalar nodes record their JSON token kind. A token change such as root `int` to root `string` is
`Incompatible`. A same-token scalar type change without an explicit classification is `Unsupported`. Numeric
changes are classified only by evidenced rules: proven range, signedness, fractional, or precision loss is
`Incompatible`; a sound widening such as `int` to `long` is `Compatible`; an unclassified numeric transition
is `Unsupported`. The public regression checks use `int` to `short` with `40000`, `int` to `uint` with `-1`,
and `decimal` to `int` with `1.5`, and verify both the report and assertion API. Nested object, collection
element, and dictionary value changes are likewise exercised through the public comparison and assertion APIs
(`R06.token-kind.public-comparison`, `R06.token-kind.numeric-boundaries.public-comparison`, and
`R07.shape.nested-contract.public-comparison`).

## Deny-by-default metadata classification

A contract is **supported** only when its recorded shape, its recorded converter and resolver metadata, and
its recorded serializer option values match an explicit allowlist of framework-known constructs. Everything
else is reported **unsupported**:

* a shape the allowlist does not recognize, and a contract whose recorded shape evidence is incomplete;
* a converter that is not a framework converter on the converter allowlist, decided by assembly identity -
  an application converter that declares a `System.Text.Json` namespace is not framework metadata;
* a metadata resolver that is not one of the allowlisted framework metadata sources, a resolver chain of more
  than one resolver, or a default reflection resolver that carries type-info modifiers, because such
  metadata can replace member converters without a declared marker;
* a scalar type that is not on the framework scalar allowlist;
* a `JsonSerializerOptions` value that is not one of the values the committed checks are measured under,
  among the recorded option families (`unsupported.option-unlisted`);
* a traversal budget that was exhausted, metadata the framework cannot produce, or any node the traversal
  could not classify.

Classification is **deny by default**, so a metadata path the walk fails to visit cannot produce a green
result. The two mechanisms that make that structural are:

1. **One traversal records everything once.** The whole reachable contract graph is recorded by a single
   recursive walk: the members of an object contract, its constructor parameter types, its captured
   extension data, the element type of every array or enumerable contract, the key and value types of every
   dictionary contract, and every type registered in `JsonPolymorphismOptions.DerivedTypes`. Every reachable
   type is recorded with the facts a verdict may rest on, and classification reads the recorded nodes only -
   it never resolves metadata a second time. A path the walk missed is therefore visible as missing evidence
   instead of as a supported contract.
2. **Shape evidence is required.** An object contract is only supported when its members and constructor
   metadata were recorded, an enumerable contract only when its element type was recorded, and a dictionary
   contract only when its key and value types were recorded. A derived type that is itself a collection,
   dictionary, or polymorphic base is walked by the same recursion, so its element, key, value, and nested
   derived types are recorded exactly like the root contract's.

Traversal is bounded at eight levels; a contract that nests deeper is recorded as unavailable and reported
unsupported rather than assumed classifiable, and a visited-type set terminates recursive contracts. A member
whose declared type is decided by a converter the allowlist does not recognize records no shape, because
resolving that shape would describe metadata the contract cannot classify.

### Classification rule table

Every verdict names one of these rules. The table is compared with the rule catalogue in code by
`D06.classification-rules.documented`, so a verdict cannot exist without a documented rule, and every rule
the run applies is checked against the catalogue by `D06.classification-rules.observed`.

| Rule | Verdict | Construct |
|---|---|---|
| `supported.resolver.default-reflection` | Supported | The metadata came from a single `DefaultJsonTypeInfoResolver` without type-info modifiers. |
| `supported.resolver.source-generated` | Supported | The metadata came from a single source-generated `JsonSerializerContext`. |
| `supported.object` | Supported | An object contract whose members, constructor parameters, and registered derived types were all recorded and are classifiable. |
| `supported.enumerable` | Supported | An enumerable contract whose element type was recorded and is classifiable. |
| `supported.dictionary` | Supported | A dictionary contract whose key and value types were recorded and are classifiable. |
| `supported.scalar` | Supported | A framework scalar type on the scalar allowlist. |
| `supported.enum` | Supported | An enum type whose wire identity was produced and whose effective converters are allowlisted. |
| `supported.member` | Supported | A member whose converters are allowlisted, whose recorded shape is classifiable, and whose enum wire identity was produced. |
| `supported.reference` | Supported | A repeated visit of a type that was already recorded and is classifiable. |
| `unsupported.metadata-unavailable` | Unsupported | The framework could not produce metadata for the type with these options. |
| `unsupported.traversal-budget` | Unsupported | The contract nests deeper than the traversal budget, so its metadata was not recorded. |
| `unsupported.resolver-unrecognized` | Unsupported | The metadata resolver is not a recognized framework metadata source. |
| `unsupported.resolver-chain` | Unsupported | The options resolve metadata through more than one resolver. |
| `unsupported.resolver-modifiers` | Unsupported | The default reflection resolver carries `JsonTypeInfo` modifiers that can rewrite metadata. |
| `unsupported.converter-unrecognized` | Unsupported | A declared or registered converter does not ship in the framework `System.Text.Json` assembly. |
| `unsupported.converter-unlisted` | Unsupported | A declared or registered converter ships in the framework assembly but is not on the converter allowlist, or is not accepted for the recorded target type. |
| `unsupported.shape-evidence-missing` | Unsupported | The recorded shape of the contract has no recorded element, key, value, member, or constructor metadata. |
| `unsupported.scalar-unlisted` | Unsupported | The recorded scalar type is not on the scalar allowlist. |
| `unsupported.enum-wire-unresolved` | Unsupported | The wire name of an enum member could not be produced from the recorded framework converter. |
| `unsupported.option-unlisted` | Unsupported | A recorded `JsonSerializerOptions` value is not one of the values the committed checks are measured under. |
| `unsupported.attribute-unlisted` | Unsupported | A declared `System.Text.Json.Serialization` serialization attribute or one of its argument values is not on the measured declared-attribute allowlist. |
| `unsupported.converter-configuration-unlisted` | Unsupported | An allowlisted framework enum converter's probed observable configuration is not on the measured converter-configuration allowlist. |

The allowlists the supported rules rest on are compared with the lists in code by
`D06.allowlist.documented`, and the framework is asked to confirm that every allowlisted scalar type resolves
to a usable framework converter while a type outside the list is rejected by the same test
(`D06.allowlist.verified`). The serializer option values the option allowlist rests on are compared with the
option profiles the committed checks are measured under and with the values the executed checks were accepted
under, in both directions, by `D06.allowlist.option-values`: a value the allowlist names but no executed check
was accepted under fails, and a value an executed check was accepted under that the allowlist does not name
fails as well.

Allowlisted framework scalar types: System.Boolean, System.Byte, System.SByte, System.Char, System.Int16, System.UInt16, System.Int32, System.UInt32, System.Int64, System.UInt64, System.Int128, System.UInt128, System.Half, System.Single, System.Double, System.Decimal, System.String, System.Guid, System.DateTime, System.DateTimeOffset, System.DateOnly, System.TimeOnly, System.TimeSpan, System.Uri, System.Version, System.Byte[], System.Memory<System.Byte>, System.ReadOnlyMemory<System.Byte>, System.Object, System.Text.Json.JsonElement, System.Text.Json.JsonDocument, System.Text.Json.Nodes.JsonNode

Allowlisted framework converters: System.Text.Json.Serialization.JsonStringEnumConverter, System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>, System.Text.Json.Serialization.JsonNumberEnumConverter<TEnum>

Allowlisted metadata resolvers: System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver, System.Text.Json.Serialization.JsonSerializerContext

Allowlisted serializer option values: numberHandling=Strict, referenceHandler=Default, defaultIgnoreCondition=Never, defaultIgnoreCondition=WhenWritingNull, unmappedMemberHandling=Skip, propertyNameCaseInsensitive=false, readCommentHandling=Disallow, allowTrailingCommas=false, maxDepth=0, dictionaryKeyPolicy=Default, ignoreReadOnlyProperties=false, ignoreReadOnlyFields=false, propertyNamingPolicy=Default, respectNullableAnnotations=false, respectRequiredConstructorParameters=false, preferredObjectCreationHandling=Replace

Allowlisted declared JSON attributes: System.Text.Json.Serialization.JsonConverterAttribute($0=System.Text.Json.Serialization.JsonStringEnumConverter) | System.Text.Json.Serialization.JsonDerivedTypeAttribute($0=<derived-type>,$1=canine) | System.Text.Json.Serialization.JsonDerivedTypeAttribute($0=<derived-type>,$1=cat) | System.Text.Json.Serialization.JsonDerivedTypeAttribute($0=<derived-type>,$1=dog) | System.Text.Json.Serialization.JsonDerivedTypeAttribute($0=<derived-type>,$1=enum) | System.Text.Json.Serialization.JsonDerivedTypeAttribute($0=<derived-type>,$1=shift) | System.Text.Json.Serialization.JsonExtensionDataAttribute | System.Text.Json.Serialization.JsonIgnoreAttribute | System.Text.Json.Serialization.JsonIgnoreAttribute(Condition=Never) | System.Text.Json.Serialization.JsonNumberHandlingAttribute($0=Strict) | System.Text.Json.Serialization.JsonPolymorphicAttribute(TypeDiscriminatorPropertyName=$type) | System.Text.Json.Serialization.JsonPolymorphicAttribute(TypeDiscriminatorPropertyName=kind) | System.Text.Json.Serialization.JsonPropertyNameAttribute($0=accountId) | System.Text.Json.Serialization.JsonPropertyNameAttribute($0=account_id) | System.Text.Json.Serialization.JsonRequiredAttribute

Allowlisted enum converter configurations: converter=System.Text.Json.Serialization.JsonStringEnumConverter,integerTokensAccepted=false | converter=System.Text.Json.Serialization.JsonStringEnumConverter,integerTokensAccepted=true | converter=default-enum-converter,integerTokensAccepted=true

An enum contract is the one framework construct that is classified without a converter being declared: a
plain enum is written by the framework numeric enum converter, and an enum converter on the allowlist is
recorded as the enum's effective converter. For an allowlisted enum converter, integer-token acceptance is
derived by executing a representative integer read and is recorded in the enum wire record; private
converter state is never inspected. A converter registered on the serializer options is otherwise classified
from its declared type identity alone - applicability is not probed - because a converter factory can claim
any type.

## Unsupported and unclassified metadata

An unrecognized converter or metadata source prevents sound classification. These cases are reported as
unsupported, are never mapped to compatible, and fail an assertion that requires a compatible result.
Converter detection is based on declared metadata only: no application converter is executed to read or
write JSON. The only serializer code that runs while a contract is recorded is the framework converter that
produces an enum member's wire name, and it runs only when every converter on the enum's effective path is on
the allowlist.

A converter is reachable through the member itself, through the element, key, and value types of a member,
through the members of a referenced contract, through a registered derived type - including a derived type
that is itself a collection, dictionary, or polymorphic base - and through the converters registered on the
serializer options. A converter is known only when it ships in the framework `System.Text.Json` assembly and
the allowlist names it.

| Rule | Case | Classification | Reason and proof |
|---|---|---|---|
| R14 | A member declares a custom converter | Unsupported | The declared converter type is not part of `System.Text.Json`. Proof: `R14.converter.opaque-property`. |
| R14b | The root type declares a custom converter | Unsupported | Same, for the whole contract. Proof: `R14.converter.opaque-root`. |
| R14c | A custom converter is registered on the serializer options | Unsupported | A registered converter the allowlist does not recognize - including a converter factory that can claim any type - makes every contract that uses those options unsupported regardless of the type it is registered against. Proof: `R14.converter.opaque-from-options`, `A01.adversarial.converter-factory`. |
| R14d | A custom metadata resolver supplies the contract | Unsupported | A custom resolver can replace a member converter without leaving a declared marker, so its metadata cannot be classified. Metadata from the default reflection resolver and from source-generated contexts is accepted. Proof: `R14.converter.custom-metadata-resolver`, `R14.converter.default-resolver-supported`, `R14.converter.source-generation-resolver-supported`. |
| R14e | A framework converter the library knows is applied | Supported | `JsonStringEnumConverter` is classified as string enum tokens rather than as opaque. Proof: `R14.converter.known-converter-supported`. |
| R14f | A collection or array element type declares a custom converter | Unsupported | `List<T>` and `T[]` are resolved to their element type and checked the same way a member's own type is. Proof: `R14.converter.opaque-collection-element`, `R14.converter.opaque-array-element`. |
| R14g | A dictionary value type declares a custom converter, or an options converter applies to it | Unsupported | Dictionary key and value types are resolved and checked. Proof: `R14.converter.opaque-dictionary-value`, `R14.converter.opaque-options-dictionary-value`. |
| R14h | A custom converter applies to the root type, or to an element, key, or value type of a root collection | Unsupported | The same walk is applied to the root type, so a contract that degrades to a single opaque token, or whose root is a collection of opaque elements, is not reported as supported. Proof: `R14.converter.opaque-root-from-options`, `R14.converter.opaque-root-element`, `R14.converter.opaque-root-dictionary-value`. |
| R14h2 | A converter attribute is declared on the type a member refers to | Unsupported | The declared type of a member is recorded like every other reachable type, so a converter on the member's type is reported unsupported whether the root contract is the type itself or a contract that refers to it. Proof: `R14.converter.opaque-type-attribute`. |
| R14h3 | A member and its declared type both declare a converter | Unsupported | Both declarations are recorded, and the first unrecognized converter found is reported. Proof: `R14.converter.opaque-member-type`. |
| R14i | An application converter declares a `System.Text.Json` namespace | Unsupported | Converter provenance is decided by assembly identity, not by namespace. Proof: `R14.converter.opaque-member-converter`. |
| R14j | A custom converter is reachable two or three levels below the member | Unsupported | Element, key, and value types and the members of referenced contracts are walked to the traversal budget. Proof: `R14.converter.opaque-nested-element-depth-2`, `R14.converter.opaque-nested-element-depth-3`, `R14.converter.opaque-nested-combination`, `R14.converter.opaque-object-graph-depth-1`, `R14.converter.opaque-object-graph-depth-2`, `R14.converter.opaque-object-graph-depth-3`. |
| R14k | A converter reachable below the first level produces a lossless round trip | Unsupported | A lossless round trip is not evidence that metadata is classifiable. Proof: `R14.converter.opaque-nested-round-trip.still-unsupported`. |
| R14l | A member nests deeper than the classification traversal budget | Unsupported | A contract that cannot be walked to its leaves cannot be classified, so exhausting the budget is reported as unsupported rather than as supported. Proof: `R14.converter.traversal-depth-limit`. |
| R14m | A registered derived type carries a member whose type declares a custom converter | Unsupported | `PolymorphismOptions.DerivedTypes` is part of the reachable metadata: the derived type's own members are walked exactly like the root contract's members. Proof: `R14.converter.opaque-polymorphic-derived-member`. |
| R14n | A converter registered on the options applies inside a registered derived type, or two derivation levels below a polymorphic member | Unsupported | The walk descends through registered derived types, so a converter that applies to a member of a derived type is found even when neither the base contract nor the member declaration mentions it. Proof: `R14.converter.opaque-polymorphic-options-derived-member`, `R14.converter.opaque-polymorphic-nested-depth-2`. |
| R14o | A dictionary key type declares a custom converter | Unsupported | Dictionary key types are recorded like value types. A key converter the library does not recognize decides what the JSON member names mean, so the contract cannot be classified from metadata even though every key is written as a string. Proof: `R14.converter.opaque-dictionary-key`. |
| R14p | A constructor parameter type declares a custom converter | Unsupported | Constructor parameter types are part of the binding metadata and are recorded by the same walk. Proof: `R14.converter.opaque-constructor-parameter`. |
| R14q | The value type captured by an extension-data member is converted by an unrecognized converter | Unsupported | An extension-data member captures arbitrary JSON, and the value type it captures with decides what is preserved; an unrecognized converter there is reported unsupported. Proof: `R14.converter.opaque-extension-data-value`. |
| R14r | The metadata provider assigns a member converter without a converter attribute | Unsupported | A resolver-assigned member converter is declared metadata that can replace a member converter, so it is recorded in addition to the converter attributes. Proof: `R14.converter.opaque-member-custom-converter`. |
| R14s | A registered derived type is itself a collection | Unsupported | The derived type is walked by the same recursion as the root contract, so its element type is recorded and an element type whose converter is opaque makes the contract unsupported. The documents of the readable and the opaque variant differ, and the wire change is measured in both directions. Proof: `R14.converter.opaque-derived-collection-element`, `R14.converter.derived-collection.wire-change`, `R14.converter.derived-shape.document`. |
| R14t | A registered derived type is itself a dictionary | Unsupported | Same, for the key and value types of a dictionary-shaped derived type. Proof: `R14.converter.opaque-derived-dictionary-value`, `R14.converter.derived-dictionary.wire-change`, `R14.converter.derived-shape.document`. |
| R14u | A registered derived type is itself a polymorphic base | Unsupported | The derived type's own `PolymorphismOptions` is recorded, so a derived type that registers further derived types is walked to those registrations. Proof: `R14.converter.opaque-derived-polymorphic-base`, `R14.converter.opaque-polymorphic-member-base`. |
| R14v | A framework converter on the allowlist is registered on the serializer options | Supported | An options-registered allowlisted framework converter is recorded only for the node type it applies to, so registering a string-enum converter records it on the enum nodes it converts and leaves an object contract supported. Proof: `R14.converter.options-allowlisted-converter-supported`. |
| U02 | A run contains compatible changes and one unclassifiable contract | Unsupported | A lossless round trip is not sufficient: a custom converter can round-trip a document and still be unclassifiable, so the result stays unsupported and the assertion fails. Proof: `R14.converter.lossless-round-trip.still-unsupported`, `U02.unsupported.never-green`. |

Every path in R14f-R14u is also recorded as a contract: the classifier reports it unsupported, the canonical
document does not describe it as supported in either its own root flag or its aggregate flag, and a report
that contains it cannot be green. The checks listed above assert all four facts for each path.

### Serializer option settings

A contract is decided by its shape, its converters, its metadata resolver - and by the serializer options it is
recorded under. The traversal therefore records the value of every `JsonSerializerOptions` setting that changes
the wire or the reading, once per contract and in a fixed family order, and the canonical document records
those values at its root. A change of an accepted option value is therefore a document difference instead of a
pair of byte-identical documents (`D08.canonical-document.options-recorded`).

The recorded families are `NumberHandling`, `ReferenceHandler`, `DefaultIgnoreCondition`,
`UnmappedMemberHandling`, `PropertyNameCaseInsensitive`, `ReadCommentHandling`, `AllowTrailingCommas`,
`MaxDepth`, `DictionaryKeyPolicy`, `IgnoreReadOnlyProperties`, `IgnoreReadOnlyFields`, `PropertyNamingPolicy`,
`RespectNullableAnnotations`, `RespectRequiredConstructorParameters`, and `PreferredObjectCreationHandling`.
The list is the one the traversal records, so a setting added to the recorded set has to be documented and
attacked like every other one.

A recorded value is accepted only when the option allowlist names it; every other value is reported through
`unsupported.option-unlisted`. The allowlist is derived from the option profiles the committed checks are
measured under rather than written out a second time, so it cannot drift from the checks, and the gate compares
it with the documented list and with the values the executed checks were accepted under, in both directions
(`D06.allowlist.option-values`). Every option family is exercised by an adversarial check that sets an unlisted
value and requires the contract to fail closed (`A01.adversarial.options-*`).

An option value that a committed check only probes as a reading behaviour - `NumberHandling =
AllowReadingFromString`, `RespectNullableAnnotations = true`, `RespectRequiredConstructorParameters = true`, or
a naming policy applied while measuring a serialized-name change, for example - is not accepted for
classification, because no committed check classifies a contract under it. Such a contract is reported
unsupported, which is the safe direction: the option allowlist never claims a value the matrix only measured
through the behaviour of a probe, and every option family keeps a value the adversarial set can attack.

### Declared serialization attributes

The traversal enumerates declared reflection attributes on every recorded contract type, constructor, member,
and enum field. Its predicate is runtime-derived: it includes every concrete or abstract class assignable to
`System.Attribute` in the `System.Text.Json.Serialization` namespace of the loaded `System.Text.Json`
assembly. It does not use `JsonAttribute` as a base-type filter, so declarations such as `JsonConstructor`
and `JsonStringEnumMemberName` cannot be silently skipped when their base hierarchy differs. The recorded
fact includes the attribute type and every constructor/named argument value. The canonical document records
the same facts on the relevant type, constructor, member, or enum-member record, and
`unsupported.attribute-unlisted` denies any declaration or argument value outside the measured allowlist.
The allowlist is derived from `DeclaredAttributeFacts` measured surfaces and is checked against the facts
accepted by the executed matrix in `D06.allowlist.attribute-values`.

The runtime declaration coverage gate enumerates the loaded assembly on every run. Each discovered declaration
type is either inventoried on a committed measured surface or explicitly excluded with a measured reason;
the current runtime inventory excludes only the abstract `JsonAttribute` base (`abstract=True; the base class
cannot be applied directly to a contract declaration`). The other 16 declaration types, including
`JsonConstructorAttribute` and `JsonStringEnumMemberNameAttribute`, are inventoried. A declaration added by a
future `System.Text.Json` runtime fails `D06.allowlist.attribute-declaration-coverage` until it has one of
those two evidence-backed dispositions.

### Runtime System.Text.Json declaration coverage

| Declaration type | Status | Measured evidence or exclusion reason |
|---|---|---|
| `System.Text.Json.Serialization.JsonAttribute` | Excluded | abstract=True; the base class cannot be applied directly to a contract declaration |
| `System.Text.Json.Serialization.JsonConstructorAttribute` | Inventoried | Constructor selection is recorded and denied; redundant and two-constructor binding probes execute. |
| `System.Text.Json.Serialization.JsonConverterAttribute` | Inventoried | Type and member converter declarations are recorded and measured. |
| `System.Text.Json.Serialization.JsonDerivedTypeAttribute` | Inventoried | Polymorphic registrations and discriminator arguments are recorded and measured. |
| `System.Text.Json.Serialization.JsonExtensionDataAttribute` | Inventoried | Extension-data declarations are recorded and measured. |
| `System.Text.Json.Serialization.JsonIgnoreAttribute` | Inventoried | Ignore declarations and condition arguments are recorded and measured. |
| `System.Text.Json.Serialization.JsonIncludeAttribute` | Inventoried | A private included member is recorded and denied. |
| `System.Text.Json.Serialization.JsonNumberHandlingAttribute` | Inventoried | Strict is accepted; WriteAsString is recorded and denied. |
| `System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute` | Inventoried | Populate on a collection member is recorded and denied. |
| `System.Text.Json.Serialization.JsonPolymorphicAttribute` | Inventoried | Polymorphism declarations and discriminator arguments are recorded and measured. |
| `System.Text.Json.Serialization.JsonPropertyNameAttribute` | Inventoried | Property-name declarations are recorded and measured. |
| `System.Text.Json.Serialization.JsonPropertyOrderAttribute` | Inventoried | A property-order declaration is recorded and denied. |
| `System.Text.Json.Serialization.JsonRequiredAttribute` | Inventoried | Required-member declarations are recorded and measured. |
| `System.Text.Json.Serialization.JsonSerializableAttribute` | Inventoried | The source-generation context declaration is inventoried from its measured context surface. |
| `System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute` | Inventoried | The source-generation context options declaration is inventoried from its measured context surface. |
| `System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute` | Inventoried | A member wire-name change is executed, recorded, and denied. |
| `System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute` | Inventoried | Disallow on a type is recorded and denied. |

The default reflection and source-generated paths were also measured against the external
`System.Runtime.Serialization` declarations `DataContractAttribute`, `DataMemberAttribute`, and
`IgnoreDataMemberAttribute`. They contribute no serialization facts and leave a matched wire unchanged, so
they are not treated as System.Text.Json declarations; a custom resolver that applies them is outside this
probe's default metadata source and is already unsupported under the resolver-modifier rule. `[Serializable]`
was separately measured as irrelevant: it contributes no serialization fact and leaves the wire unchanged.
The walk separately measures effective metadata such as requiredness and source-generated options through
`JsonTypeInfo`. No other outside-namespace declaration is claimed here without a measurement.

The measured accepted declarations include the existing property-name, requiredness, extension-data,
polymorphism, and allowlisted enum-converter attributes, plus `JsonNumberHandling(Strict)` and
`JsonIgnore(Condition = Never)`. `JsonNumberHandling(WriteAsString)` is deliberately unlisted: both type-level
and member-level declarations are recorded, change the canonical document, change the wire token, and are
denied. `JsonIgnore(Condition = WhenWritingDefault)` is likewise recorded, changes the document, and is denied.
`JsonConstructor` is also deliberately unlisted: regardless of its attribute base hierarchy, a redundant declaration changes the canonical document and a
two-constructor declaration changes binding from `Quantity = 1` to `Quantity = 7`, so both are denied even when
the wire is unchanged in the redundant case. `JsonInclude`, `JsonObjectCreationHandling`, `JsonPropertyOrder`,
`JsonUnmappedMemberHandling`, and `JsonStringEnumMemberName` are recorded and denied by executed probes; the
last changes the wire name from `Created` to `created-order`. The executed source-generated constructor probe
(`R13.source-generation.constructor-attribute`) records the `JsonConstructor` fact in both reflection and
source-generated documents, observes the generated binding of `Quantity = 7`, and denies both documents.

### Allowlisted enum converter configuration

For an allowlisted framework enum converter, `WriteEnumToken` and the integer-token acceptance fact are both
derived by executing the effective converter. The canonical enum wire record carries the converter identity
and the measured `integerTokensAccepted` result. The accepted configurations are the default numeric enum
converter with integer acceptance, `JsonStringEnumConverter()` with integer acceptance, and
`JsonStringEnumConverter(allowIntegerValues: false)` without integer acceptance. The two string-enum
configurations produce different canonical documents, and an allowlisted but unmeasured configuration such as
`JsonNumberEnumConverter<TEnum>` is reported through `unsupported.converter-configuration-unlisted`.

### Adversarial checks

The adversarial set `A01.adversarial.*` tries to keep opaque metadata behind a path no rule names, and every
case must fail closed. The recorded outcomes are:

| Attack | Contract | Outcome |
|---|---|---|
| `A01.adversarial.derived-collection` | A registered derived type that is a collection of opaque elements | Unsupported |
| `A01.adversarial.derived-dictionary` | A registered derived type that is a dictionary of opaque values | Unsupported |
| `A01.adversarial.derived-polymorphic-base` | Polymorphism nested inside a registered derived type | Unsupported |
| `A01.adversarial.polymorphic-member` | A polymorphic type reached through a member | Unsupported |
| `A01.adversarial.polymorphic-collection` | A collection of polymorphic values | Unsupported |
| `A01.adversarial.polymorphic-dictionary` | A dictionary of polymorphic values | Unsupported |
| `A01.adversarial.derived-member-converter` | A member-level converter inside a registered derived type | Unsupported |
| `A01.adversarial.converter-factory` | A converter factory registered on the options | Unsupported |
| `A01.adversarial.type-info-modifier` | A `JsonTypeInfo` customization callback that replaces a member converter | Unsupported |
| `A01.adversarial.resolver-chain` | A custom `TypeInfoResolver` chain | Unsupported |
| `A01.adversarial.generic-argument` | A converter declared on a type used as a generic argument | Unsupported |
| `A01.adversarial.unlisted-scalar-type` | A scalar member type outside the scalar allowlist | Unsupported |
| `A01.adversarial.options-number-handling` | `NumberHandling = WriteAsString` and `NumberHandling = AllowReadingFromString` | Unsupported |
| `A01.adversarial.options-reference-handler` | `ReferenceHandler = Preserve` | Unsupported |
| `A01.adversarial.options-default-ignore-condition` | `DefaultIgnoreCondition = WhenWritingDefault` | Unsupported |
| `A01.adversarial.options-unmapped-member-handling` | `UnmappedMemberHandling = Disallow` | Unsupported |
| `A01.adversarial.options-property-name-case-insensitive` | `PropertyNameCaseInsensitive = true` | Unsupported |
| `A01.adversarial.options-read-comment-handling` | `ReadCommentHandling = Skip` | Unsupported |
| `A01.adversarial.options-allow-trailing-commas` | `AllowTrailingCommas = true` | Unsupported |
| `A01.adversarial.options-max-depth` | `MaxDepth = 8` | Unsupported |
| `A01.adversarial.options-dictionary-key-policy` | `DictionaryKeyPolicy = CamelCase` | Unsupported |
| `A01.adversarial.options-ignore-read-only-properties` | `IgnoreReadOnlyProperties = true` | Unsupported |
| `A01.adversarial.options-ignore-read-only-fields` | `IgnoreReadOnlyFields = true` | Unsupported |
| `A01.adversarial.options-property-naming-policy` | `PropertyNamingPolicy = CamelCase` | Unsupported |
| `A01.adversarial.options-respect-nullable-annotations` | `RespectNullableAnnotations = true` | Unsupported |
| `A01.adversarial.options-respect-required-constructor-parameters` | `RespectRequiredConstructorParameters = true` | Unsupported |
| `A01.adversarial.options-preferred-object-creation-handling` | `PreferredObjectCreationHandling = Populate` | Unsupported |
| `A01.adversarial.attributes-number-handling-type` | A type-level `[JsonNumberHandling(WriteAsString)]` declaration | Unsupported |
| `A01.adversarial.attributes-number-handling-member` | A member-level `[JsonNumberHandling(WriteAsString)]` declaration | Unsupported |
| `A01.adversarial.attributes-ignore-condition` | A member-level `[JsonIgnore(Condition = WhenWritingDefault)]` declaration | Unsupported |
| `A01.adversarial.attributes-constructor-redundant` | A redundant `[JsonConstructor]` declaration on a type with one public parameterized constructor | Unsupported |
| `A01.adversarial.attributes-constructor-binding` | A `[JsonConstructor]` declaration that changes selection between two public constructors | Unsupported |
| `A01.adversarial.attributes-include` | A private member included by `[JsonInclude]` | Unsupported |
| `A01.adversarial.attributes-object-creation-handling` | A collection member declares `[JsonObjectCreationHandling(Populate)]` | Unsupported |
| `A01.adversarial.attributes-property-order` | A member declares `[JsonPropertyOrder]` | Unsupported |
| `A01.adversarial.attributes-string-enum-member-name` | An enum member declares `[JsonStringEnumMemberName]` and changes its string wire name | Unsupported |
| `A01.adversarial.attributes-unmapped-member-handling` | A type declares `[JsonUnmappedMemberHandling(Disallow)]` | Unsupported |
| `A01.adversarial.attributes-runtime-serialization` | `System.Runtime.Serialization` declarations are measured as irrelevant to the default reflection and source-generated System.Text.Json contracts | Supported |
| `A01.adversarial.attributes-serializable-irrelevant` | `[Serializable]` is measured as irrelevant to the System.Text.Json contract | Supported |
| `A01.adversarial.converter-configuration-unlisted` | An allowlisted `JsonNumberEnumConverter<TEnum>` configuration outside the measured probe set | Unsupported |

### Metadata discovery path inventory

The table below is the inventory of every metadata-resolution path the classifier walks. It is the
developer-facing record of the single recursive walk: each row names a discovery source, a check that proves
opaque metadata reached through it is reported unsupported, and how the path is bounded. The gate check
`D06.discovery-paths.inventory` compares this table with the sources the traversal actually visited and with
the executed checks, so a discovery source that is added to the walk without an inventory row, a check, or a
matching path binding fails the matrix instead of passing by default.

| Discovery source | Check that proves it | How the path is bounded or why it cannot hide opaque metadata |
|---|---|---|
| `type-converter-attribute` | `R14.converter.opaque-type-attribute` | The converter attribute on the contract type or on any visited type, including a member's declared type. Bounded by the shared visited-type set and the traversal budget. |
| `member-converter-attribute` | `R14.converter.opaque-member-converter` | The converter attribute on the member. Bounded because a member is recorded once per declaring contract. |
| `member-custom-converter` | `R14.converter.opaque-member-custom-converter` | The converter the metadata provider assigned to a member without an attribute. Checked against framework provenance exactly like an attribute converter. |
| `options-converters` | `R14.converter.opaque-root-from-options` | Every converter registered in `JsonSerializerOptions.Converters`, recorded in declared converter type order. Bounded by the visited-type set. |
| `options-settings` | `A01.adversarial.options-number-handling` | The value of every `JsonSerializerOptions` setting that changes the wire or the reading, recorded once per contract in a fixed family order and compared with the option allowlist. Bounded because every family is read from the same option set for every visited node, and a value the allowlist does not name is reported unsupported. |
| `declared-attributes` | `A01.adversarial.attributes-number-handling-type` | Every `System.Text.Json.Serialization` attribute is enumerated at runtime from each recorded type, constructor, member, and enum field, then checked against the measured attribute allowlist. Reflection supplies the declarations for both reflection and source-generated `JsonTypeInfo`; source-generated context behavior is executed separately, and its limitation is documented below. |
| `converter-configuration` | `R08.enum.converter-configuration` | Only allowlisted framework enum converters are probed. Their integer-token acceptance is derived by executing a representative read and compared with the measured configuration allowlist; unknown and opaque converters remain unsupported. |
| `enumerable-element-types` | `R14.converter.opaque-collection-element` | The element type of every visited array or enumerable type, resolved from the framework element type with the generic shape as the fallback. Bounded by the traversal budget. |
| `dictionary-key-types` | `R14.converter.opaque-dictionary-key` | The key type of every visited dictionary type. Bounded by the traversal budget. |
| `dictionary-value-types` | `R14.converter.opaque-dictionary-value` | The value type of every visited dictionary type. Bounded by the traversal budget. |
| `object-members` | `R14.converter.opaque-member-type` | The members of every visited object contract, including members inherited from a base type. Bounded by the traversal budget and the visited-type set. |
| `extension-data` | `R14.converter.opaque-extension-data-value` | The captured value type of every `[JsonExtensionData]` member. Bounded because the captured type is recorded once. |
| `constructor-parameters` | `R14.converter.opaque-constructor-parameter` | The parameter types of every public constructor on a visited object contract. Bounded by the traversal budget. |
| `polymorphism-derived-types` | `R14.converter.opaque-polymorphic-derived-member` | Every type registered in `PolymorphismOptions.DerivedTypes`, including registrations made by a derived type and derived types that are themselves collections or dictionaries. Bounded by the traversal budget and the visited-type set. |
| `resolver-chain` | `R14.converter.custom-metadata-resolver` | The identity of the metadata resolver and the length of the resolver chain: the default reflection resolver without modifiers and source-generated contexts are recognized, any other resolver, chain, or modifier set is reported unsupported because it can replace member converters without a declared marker. |

Every source in this inventory is reachable by the same recursive walk. The `object-members`,
`polymorphism-derived-types`, `enumerable-element-types`, and `dictionary-*-types` rows are the recursive
ones, and a derived type is expanded by the same kind switch as the root contract, so a derived type that is
itself a collection, dictionary, or polymorphic base cannot stop the recursion.

## Measurement harness and document comparison

The experiment's own measurement behavior is part of the evidence: a probe that cannot run must not be able to
pass as either a compatible or an incompatible reading, and an already-unsupported member must not be
resolved further by the contract model.

| Rule | Case | Expectation | Evidence |
|---|---|---|---|
| R15 | A probe raises an exception the harness does not expect | The probe is recorded as a fault, and every check built on the probe fails | `R15.probe.unexpected-exception` |
| R15b | A contract rejects a document because a member is required | The probe is recorded as a rejection, not a fault, and the incompatible expectation holds | `R15.probe.contract-rejection` |
| R15c | A member is already classified unsupported | No shape is resolved for that member, while a classifiable complex member still records its shape | `R15.canonical-document.unsupported-member-shape-skipped` |
| R15d | Some reachable metadata is unsupported | The document carries an aggregate support state that is unsupported when any reachable metadata is unsupported, so a report layer never infers safety from the root flag alone | `R15.canonical-document.aggregate-support-state` |
| C01 | A number keeps its value but changes serialized form | Numbers compare by exact value: `5` and `5.0`, and `1e2` and `100`, are equal, while a changed value or a changed token kind is a difference | `C01.document-comparison.numbers-by-value`, `C01.document-comparison.number-and-token-changes` |
| C01b | The written digits differ although a `decimal` or `double` parse would round both sides to the same value | The exact comparison reports a difference: `10000000000000000000000000000001` versus `10000000000000000000000000000000`, `123456789012345678901234567890.1` versus `123456789012345678901234567890.2`, and `0.100000000000000000000000000001` versus `0.1` | `C01.document-comparison.exact-value` |
| C01c | A number is outside the range `decimal` and finite `double` parsing can represent | The exact comparison reports a difference instead of falling back to a rounded or non-finite parse: `1E+400` versus `1E+401`, and `1E-400` versus `0` | `C01.document-comparison.non-finite-numbers` |

## Canonical contract document

The canonical document describes the effective contract in a stable form: format version `2` for the current
schema, members sorted by name, a fixed key order, complete nested object/element/key/value nodes, and
references for repeated types so recursive contracts terminate within the existing traversal budget. Scalar
nodes record their JSON token kind. The document uses LF line endings, UTF-8 without a byte order mark, and
contains no timestamps, host paths, or process-specific values. An enum member also records its wire identity
- the serialized name the applied framework converter produces, or the numeric value - so a change that only
alters an enum member's wire name is visible in the document instead of producing identical bytes.

Every node record states the discovery source that reached it (`reachedBy`), the path from the root contract
(`path`), the classification rule that decided it (`rule`), and its support state (`supported`), and an
unsupported record carries the recorded `reason`. A member record states its rule, its support state, and its
recorded wire identity, shape, and requiredness. A record of a repeated type is written as a `reference` to
the path where the type was first recorded, which is how recursive contracts terminate.

The root record also states the whole-contract support verdict, which includes every nested record, and
`overallSupported` restates that verdict at document level. A report layer reads `overallSupported` instead
of inferring safety from the root flag, because a contract can have a classifiable root and still contain
unsupported metadata below it.

The document root also records the serializer option values the contract was recorded under, in the fixed
order of the option families. An option value that the allowlist accepts is part of the document, so two
contracts that keep the same shape and differ only in an accepted option value produce different documents
(`D08.canonical-document.options-recorded`), and an option value the allowlist does not accept is recorded with
the unsupported verdict and its rule (`unsupported.option-unlisted`).

Type and member records also carry the enumerated declared JSON serialization attributes, including their
argument values. An unlisted declaration is recorded with `unsupported.attribute-unlisted`; an accepted
declaration is still visible even when it has no wire effect. Enum wire records additionally carry the
allowlisted converter identity and the executed integer-token acceptance result, so the two measured
`JsonStringEnumConverter` configurations cannot collapse to one canonical document.

A registered derived type is described with its discriminator, its type name, its support state, and its
recorded content: its members, its own element, key, or value types when the derived type is a collection or
a dictionary, and its own registered derived types when the derived type is itself a polymorphic base. A
wire-visible change inside a derived type therefore changes the canonical document instead of leaving it
byte-identical.

| Rule | Property | Evidence |
|---|---|---|
| D01 | Repeated canonicalization of the same contract is byte-identical | Three independently created option sets produce the same document (`D01.canonical-document.repeatable`); cross-process equality is checked by `scripts/validate.ps1`. |
| D01b | The written document is LF-terminated, contains no carriage returns, and has no UTF-8 byte order mark | The check asserts on the bytes written by the same code path the validation script compares (`D01.canonical-document.line-endings`). |
| D01c | The document contains no host paths | `D01.canonical-document.host-independent`. |
| D02 | Equivalent option sets produce identical documents regardless of converter registration order | `D02.canonical-document.options-equivalent`. |
| D03 | A contract that refers to its own type canonicalizes deterministically | `D03.canonical-document.recursive-type`. |
| D04 | The counts quoted by the scope documentation are derived from the matrix output rather than counted by hand | `D04.matrix.check-count`, `D04.policy.full-compatible-count`, `D04.policy.unsupported-count`. |
| D05 | The document records the wire identity of every enum member: its serialized name when the applied framework converter writes strings, or its numeric value. The name is produced by the effective converter, so a member-level converter declaration takes precedence over the contract options, and two contracts whose wire is identical record identical identities | `R08.enum.string-tokens.wire-identity`, `R08.enum.numeric.wire-identity`, `R08.enum.naming-policy.document`, `R08.enum.member-rename.document`, `R08.enum.member-level.wire-identity`. |
| D06 | Every metadata-resolution path the classifier walks is inventoried, documented, covered by an executed check, and bounded; every classification rule, node kind, and allowlist of the deny-by-default classifier - including serializer options, runtime-enumerated declared attributes, and converter configurations - is bound to this document and to the values the executed checks were accepted under; and a source, node kind, rule, or loaded System.Text.Json declaration added without coverage fails the gate | `D06.discovery-paths.inventory`, `D06.classification-rules.documented`, `D06.classification-rules.observed`, `D06.allowlist.documented`, `D06.allowlist.verified`, `D06.allowlist.option-values`, `D06.allowlist.attribute-values`, `D06.allowlist.attribute-declaration-coverage`, `D06.allowlist.converter-configurations`, together with the per-path checks listed in the path inventory. |
| D07 | A registered derived type's member content, element, key, and value types, and nested registrations are part of the canonical document | `R10.polymorphism.derived-member.document`, `R14.converter.opaque-polymorphic-derived-member`, `R14.converter.derived-shape.document`. |
| D08 | The canonical document records serializer option values, declared JSON attributes, and probed enum converter configuration, so a change of an accepted recorded fact is a document difference instead of a pair of byte-identical documents | `D08.canonical-document.options-recorded`, `D08.canonical-document.attributes-recorded`, `R08.enum.converter-configuration`. |

## Documented limitations

1. **Semantic compatibility is out of scope.** Inserting an enum member into a numeric representation is
   wire-compatible and round-trips unchanged, but the value that used to denote one member may now denote
   another (`R08.enum.member-insertion`). JsonDrift reports structural compatibility and cannot detect a
   change of meaning, a changed unit, or a changed business rule.
2. **Reference nullability alone does not change reading.** A nullable-to-non-nullable reference member is a
   metadata change, not a reading failure, unless the reader enables nullable annotation enforcement, in
   which case the same document is rejected. Both measurements are recorded above.
3. **Converter detection is declaration-based.** Converters are classified from the converter attribute on the
   member, on the member type, on a reachable element, key, or value type, on a registered derived type or
   one of its members, or on the root type, from converters the metadata provider assigns to a member, from
   converters registered on the options, and from the identity of the metadata resolver. Application
   converters are never executed: a declared or registered converter that is not on the allowlist leaves the
   contract unsupported, and an enum wire name is only produced by an allowlisted framework enum converter.
   Reachable types are walked to a depth of eight; a contract that nests deeper, or whose referenced metadata
   cannot be resolved, is reported as unsupported rather than inspected further.
4. **Only measured behavior is claimed.** A serializer feature that is not listed in this document has no
   classification and must be reported as unsupported or unclassified until it is measured. The scalar
   allowlist is the set of framework scalar types the matrix has verified, and the option-value allowlist is
   the set of `JsonSerializerOptions` values the committed checks are measured under; a framework scalar type,
   or an option value, outside its allowlist is reported unsupported until a rule and a check are added for
   it. The option allowlist is per option family, not per combination: a contract that combines two accepted
   values is accepted, even when no committed check measures that exact combination, because each individual
   value is proven.
5. **A string enum member's wire name is read from the serializer.** The name is produced by the framework
   string-enum converter that the member's effective declared metadata applies - the converter declared on
   the member first, then the converter declared on the enum type or registered on the options - which is how
   an applied naming policy becomes observable. Application converters are never executed, and a string enum
   member whose wire name cannot be produced is reported as unsupported; that member keeps the contract
   unsupported through the aggregate support state.
6. **The walk records framework metadata, not application behavior.** Classification is a statement about the
   recorded contract metadata: it does not run the application's converters, and the only serializer calls it
   performs while recording are the allowlisted framework enum converter's wire-name write and its representative
   integer-token read. `JsonTypeInfo` does not expose reflection-declared attributes for a source-generated
   context. The matrix therefore reads the runtime-enumerated System.Text.Json declaration facts from the
   generated contract type, its constructors, reflected members, and enum fields, and executes reflection and
   source-generated probes for the constructor declaration. The context's `JsonSourceGenerationOptions` and
   `JsonSerializable` declarations are inventoried from the measured context surface, while their effective
   generated options and registered-contract behavior are measured through `JsonTypeInfo.Options` and the
   generated type infos. This distinction documents the metadata source limitation honestly: reflection is the
   declaration source, and source generation proves the resulting effective contract behavior rather than
   exposing a separate declaration list.
7. **Baseline storage, limits, and version migration are not covered here.** This document defines
   classification semantics only.

## Reproducing the evidence

```pwsh
dotnet restore KeelMatrix.JsonDrift.sln --configfile NuGet.config
dotnet build KeelMatrix.JsonDrift.sln -c Release --no-restore
dotnet run --project experiments/KeelMatrix.JsonDrift.RuleMatrix -c Release --no-build -- --matrix --verbose
```

The experiment prints every check with its expected classification and the measured result, and exits with a
non-zero code when any measurement disagrees with the recorded classification. The measurements recorded here
were produced on .NET 8.0.31 with `System.Text.Json` 10.0.12 (assembly version 10.0.0.0). The committed
validation workflow runs these measurements and the package-consumer gate on GitHub-hosted `windows-latest`,
`ubuntu-latest`, and `macos-latest` for the pinned `net8.0` SDK/runtime combination. Other runtime and SDK
combinations are outside the validated platform claim.
