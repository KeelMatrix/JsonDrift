namespace KeelMatrix.JsonDrift.Internal;

/// <summary>One family of wire-contract facts populated by the metadata traversal.</summary>
internal sealed record ContractFactFamily(
    string Id,
    string Name,
    IReadOnlyList<string> FactKinds);

/// <summary>
/// The classification of one unordered pair of fact families. Interacting pairs name the composed rule and
/// a wire witness; non-interacting pairs state why neither family can change the other's preservation rule.
/// </summary>
internal sealed record ContractFactInteraction(
    string EarlierFamily,
    string LaterFamily,
    string? Rule,
    string? Witness,
    string? NonInteractionReason)
{
    public string Id => $"{EarlierFamily} + {LaterFamily}";
}

/// <summary>
/// Exhaustive pairwise coverage for the contract-fact families. A family or compared fact kind added without
/// classifying every pair fails <see cref="ValidateCoverage"/>.
/// </summary>
internal static class ContractFactInteractions
{
    private const string IndependentMemberAxes =
        "the facts are checked independently after member selection, and the worst verdict is retained; neither fact changes the JSON key or token evaluated by the other";
    private const string DifferentNodeKinds =
        "the families apply to mutually exclusive canonical node kinds, so one node cannot carry both facts; nested nodes are covered through the reference-graph pairs";
    private const string PresenceBeforeValue =
        "the presence rule is decided before a value token is interpreted, and the value-domain rule cannot make a missing member present";
    private const string RecursiveIndependence =
        "the nested contract is compared recursively before outcomes are aggregated, and neither family changes the edge used to reach the other";

    public static IReadOnlyList<ContractFactFamily> Families { get; } = new ContractFactFamily[]
    {
        new(
            "member-identity",
            "member name and serialized name",
            new[] { "member serialized identity" }),
        new(
            "member-inclusion",
            "member inclusion",
            new[] { "member inclusion" }),
        new(
            "requiredness",
            "requiredness",
            new[] { "member requiredness" }),
        new(
            "nullability",
            "nullability and null-token acceptance",
            new[] { "member nullable annotations", "value-slot null acceptance" }),
        new(
            "member-materialization",
            "member writability and materialization",
            new[] { "member materialization capability", "constructor binding" }),
        new(
            "token-domain",
            "token kind and numeric range",
            new[] { "wire-bearing CLR identity" }),
        new(
            "container-shape",
            "dictionary and collection shape",
            new[] { "node JSON shape" }),
        new(
            "container-materialization",
            "dictionary and collection materialization",
            new[] { "collection ordering and multiplicity", "dictionary materialization capability" }),
        new(
            "enum-domain",
            "enum named and integer token domains",
            new[] { "enum token domain" }),
        new(
            "extension-data",
            "extension-data presence and key/value space",
            new[] { "extension-data capture" }),
        new(
            "reference-graph",
            "reference and nested contract graph",
            new[] { "nested contract graph", "metadata completeness" }),
        new(
            "serializer-configuration",
            "serializer options, converters, and resolvers",
            new[] { "serializer option set", "resolver classification", "converter classification" }),
        new(
            "polymorphism",
            "polymorphic discriminator and derived-type contract",
            new[] { "polymorphic derived-type contract" }),
    };

    public static IReadOnlyList<ContractFactInteraction> Entries { get; } = new ContractFactInteraction[]
    {
        Interacts("member-identity", "member-inclusion", "R03 serialized identity is evaluated only for effective members; R09 inclusion is still reported independently", "test:IndependentMemberConstraintsAreReportedTogether"),
        Interacts("member-identity", "requiredness", "a rename destination must satisfy R04 requiredness before R03 extension-data preservation can be accepted", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        NonInteracting("member-identity", "nullability", IndependentMemberAxes),
        NonInteracting("member-identity", "member-materialization", IndependentMemberAxes),
        Interacts("member-identity", "token-domain", "rename matching requires the same recorded token domain; otherwise the change is removal plus addition and fails closed", "matrix:R03.serialized-name.json-property-name"),
        Interacts("member-identity", "container-shape", "rename matching requires the same declared container shape before R03 can apply", "matrix:R03.serialized-name.json-property-name"),
        NonInteracting("member-identity", "container-materialization", "container materialization is classified on the selected member shape and cannot change which serialized name selected that member"),
        NonInteracting("member-identity", "enum-domain", "enum tokens are compared after a member is selected; enum metadata cannot select or alias a JSON property name"),
        Interacts("member-identity", "extension-data", "R03 allows a rename only when the old key is preserved by later extension data and the earlier extension key space cannot shadow the destination", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("member-identity", "reference-graph", "serialized identity is reapplied at every recursively reached object node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("member-identity", "serializer-configuration", "property naming options participate in R03 and unmeasured resolver or modifier changes fail closed", "matrix:R03.serialized-name.naming-policy"),
        NonInteracting("member-identity", "polymorphism", "member names and discriminator values occupy separate canonical namespaces; derived object members are compared through the reference graph"),

        Interacts("member-inclusion", "requiredness", "an included destination still has its independent R04 requiredness checked after R09", "test:IndependentMemberConstraintsAreReportedTogether"),
        NonInteracting("member-inclusion", "nullability", PresenceBeforeValue),
        Interacts("member-inclusion", "member-materialization", "an included member must retain materialization even when R09 also changes", "test:IndependentMemberConstraintsAreReportedTogether"),
        NonInteracting("member-inclusion", "token-domain", PresenceBeforeValue),
        NonInteracting("member-inclusion", "container-shape", PresenceBeforeValue),
        NonInteracting("member-inclusion", "container-materialization", PresenceBeforeValue),
        NonInteracting("member-inclusion", "enum-domain", PresenceBeforeValue),
        Interacts("member-inclusion", "extension-data", "only an effective named member can shadow an earlier extension-data key; ignored additions leave the key in extension data", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("member-inclusion", "reference-graph", "R09 inclusion is evaluated independently at every recursively reached object node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("member-inclusion", "serializer-configuration", "ignore options and attributes decide effective inclusion and unmeasured values fail closed", "matrix:R09.ignore.condition-when-writing-null"),
        NonInteracting("member-inclusion", "polymorphism", "member inclusion does not register or remove discriminator values; included members of derived objects are reached through the reference graph"),

        Interacts("requiredness", "nullability", "R04 distinguishes missing members from explicit null tokens, while R05 independently checks whether null can be read", "matrix:R04.requiredness.explicit-null"),
        Interacts("requiredness", "member-materialization", "constructor-bound additions retain both their presence requirement and their materialization constraint", "test:IndependentMemberConstraintsAreReportedTogether"),
        NonInteracting("requiredness", "token-domain", PresenceBeforeValue),
        NonInteracting("requiredness", "container-shape", PresenceBeforeValue),
        NonInteracting("requiredness", "container-materialization", PresenceBeforeValue),
        NonInteracting("requiredness", "enum-domain", PresenceBeforeValue),
        Interacts("requiredness", "extension-data", "capturing an old key as extension data cannot satisfy a differently named required destination member", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("requiredness", "reference-graph", "R04 is evaluated at every recursively reached object node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("requiredness", "serializer-configuration", "required-constructor enforcement is an option-dependent presence constraint", "matrix:R12.binding.constructor-parameter-added.enforced"),
        NonInteracting("requiredness", "polymorphism", "required object members and registered discriminator values are separate presence checks; derived member requirements are reached recursively"),

        NonInteracting("nullability", "member-materialization", IndependentMemberAxes),
        Interacts("nullability", "token-domain", "R05 null-token acceptance is checked before the non-null R06 token domain", "test:NullableValueAcceptanceIsRetainedAtEveryValueSlot"),
        NonInteracting("nullability", "container-shape", "null acceptance belongs to the value slot outside its non-null container shape, so both checks run without either relaxing the other"),
        NonInteracting("nullability", "container-materialization", "null rejection occurs before a container materializer is invoked and cannot be repaired by that materializer"),
        NonInteracting("nullability", "enum-domain", "the null token lies outside both named and integer enum domains and is checked independently at the value slot"),
        Interacts("nullability", "extension-data", "an earlier extension-data value can be null while a later named non-nullable member shadows that key and rejects the token", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("nullability", "reference-graph", "null acceptance is retained on every edge before reference resolution", "test:NullableValueAcceptanceIsRetainedAtEveryValueSlot"),
        Interacts("nullability", "serializer-configuration", "nullable-annotation enforcement is recorded from serializer options and compared by R05/R13", "matrix:R05.nullability.reference-nullable-removed.enforced"),
        NonInteracting("nullability", "polymorphism", "a null token is handled before discriminator dispatch, while non-null derived contracts are compared recursively"),

        NonInteracting("member-materialization", "token-domain", IndependentMemberAxes),
        NonInteracting("member-materialization", "container-shape", "member writability controls assignment of the selected value; it does not alter that value's JSON shape"),
        Interacts("member-materialization", "container-materialization", "a writable member is preservable only when its selected collection or dictionary can also be materialized", "test:DictionaryMaterializationInteractionsFailClosed"),
        NonInteracting("member-materialization", "enum-domain", IndependentMemberAxes),
        Interacts("member-materialization", "extension-data", "R03 extension-data preservation is usable only when the capture member itself has a measured materialization path", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("member-materialization", "reference-graph", "member materialization is checked at every recursively reached object node", "test:RemovingMemberMaterializationCapabilityDoesNotReportCompatible"),
        Interacts("member-materialization", "serializer-configuration", "constructor and populate options participate in the effective materialization path and unmeasured values fail closed", "matrix:A01.adversarial.options-preferred-object-creation-handling"),
        NonInteracting("member-materialization", "polymorphism", "member assignment occurs after derived-type dispatch; materialization within a derived object is checked through the reference graph"),

        Interacts("token-domain", "container-shape", "R06 scalar-token changes and R07 container-shape changes share the same node dispatch and neither can mask the other", "matrix:R06.token-kind.public-comparison"),
        NonInteracting("token-domain", "container-materialization", DifferentNodeKinds),
        Interacts("token-domain", "enum-domain", "R08 records whether the enum accepts integer or named string tokens before R06 compares its token kind", "matrix:R08.enum.number-to-string.tokens-only"),
        Interacts("token-domain", "extension-data", "an earlier extension-data value can carry any token kind and a later named member may bind and reject it", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("token-domain", "reference-graph", "R06 token domains are compared at roots and every recursively reached value node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("token-domain", "serializer-configuration", "number handling and converters can change accepted token domains; unmeasured configurations fail closed", "matrix:A01.adversarial.options-number-handling"),
        NonInteracting("token-domain", "polymorphism", "discriminator dispatch selects a derived node before that node's scalar or member token domains are compared recursively"),

        Interacts("container-shape", "container-materialization", "R07 support requires both matching key/value or element shape and a measured later materializer", "test:DictionaryMaterializationInteractionsFailClosed"),
        NonInteracting("container-shape", "enum-domain", DifferentNodeKinds),
        Interacts("container-shape", "extension-data", "an earlier extension-data value can carry object or array shape while a later named scalar member shadows that key and rejects it", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("container-shape", "reference-graph", "collection elements and dictionary key/value nodes are recursively traversed and compared", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("container-shape", "serializer-configuration", "dictionary-key policy and converters can change container wire shape or key space and unmeasured values fail closed", "matrix:A01.adversarial.options-dictionary-key-policy"),
        Interacts("container-shape", "polymorphism", "registered derived types can themselves be collections or dictionaries and must be traversed as those shapes", "matrix:A01.adversarial.derived-dictionary"),

        NonInteracting("container-materialization", "enum-domain", DifferentNodeKinds),
        Interacts("container-materialization", "extension-data", "R03 extension-data preservation requires the capture dictionary to pass the ordinary dictionary materialization gate", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("container-materialization", "reference-graph", "materialization support is required at root and nested container nodes reached through members", "test:DictionaryMaterializationInteractionsFailClosed"),
        Interacts("container-materialization", "serializer-configuration", "object-creation handling can select a different container population path and unmeasured values fail closed", "matrix:A01.adversarial.options-preferred-object-creation-handling"),
        Interacts("container-materialization", "polymorphism", "a collection- or dictionary-shaped derived type must pass its own materialization gate after discriminator dispatch", "matrix:A01.adversarial.derived-dictionary"),

        Interacts("enum-domain", "extension-data", "an earlier extension-data key can carry a token outside a later named enum member's accepted domain", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("enum-domain", "reference-graph", "enum named and integer domains are recorded at roots and nested value slots", "test:EnumIntegerTokenAcceptanceIsComparedAtRootAndNestedSlots"),
        Interacts("enum-domain", "serializer-configuration", "enum converters and their integer-token setting define the recorded R08 domain", "matrix:R08.enum.converter-configuration"),
        NonInteracting("enum-domain", "polymorphism", "enum value tokens and polymorphic discriminator tokens are selected at different nodes; derived enum members are compared recursively"),

        Interacts("extension-data", "reference-graph", "extension-data key/value contracts are traversed recursively and their unsupported descendants fail the whole contract", "matrix:R14.converter.opaque-extension-data-value"),
        Interacts("extension-data", "serializer-configuration", "unmapped-member handling and resolver changes can alter capture behavior, so unmeasured settings fail closed", "matrix:A01.adversarial.options-unmapped-member-handling"),
        Interacts("extension-data", "polymorphism", "adding polymorphic dispatch can reinterpret an earlier extension-data key as the discriminator and reject an unrecognized value", "test:ExtensionDataUnknownDiscriminatorFailsClosedWhenPolymorphismIsAdded"),

        Interacts("reference-graph", "serializer-configuration", "resolver, converter, and option gates are applied to every recursively reached node", "matrix:R14.converter.opaque-object-graph-depth-2"),
        Interacts("reference-graph", "polymorphism", "registered derived types and nested polymorphic bases are part of the bounded recursive graph", "matrix:A01.adversarial.derived-polymorphic-base"),

        Interacts("serializer-configuration", "polymorphism", "resolver modifiers and converters can rewrite derived metadata, so only measured configuration is allowed", "matrix:A01.adversarial.type-info-modifier"),
    };

    public static IReadOnlyList<string> ValidateCoverage(
        IReadOnlyList<ContractFactFamily>? families = null,
        IReadOnlyList<ContractFactInteraction>? entries = null)
    {
        families ??= Families;
        entries ??= Entries;

        var errors = new List<string>();
        string[] familyIds = families.Select(static family => family.Id).ToArray();
        string[] comparedFacts = ComparisonFactRules.Entries
            .Where(static entry => entry.ComparedRule is not null)
            .Select(static entry => entry.FactKind)
            .OrderBy(static fact => fact, StringComparer.Ordinal)
            .ToArray();
        string[] assignedFacts = families
            .SelectMany(static family => family.FactKinds)
            .OrderBy(static fact => fact, StringComparer.Ordinal)
            .ToArray();

        errors.AddRange(familyIds
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => $"fact family id is duplicated: {group.Key}"));
        errors.AddRange(comparedFacts.Except(assignedFacts, StringComparer.Ordinal)
            .Select(static fact => $"compared fact has no interaction family: {fact}"));
        errors.AddRange(assignedFacts.Except(comparedFacts, StringComparer.Ordinal)
            .Select(static fact => $"interaction family names no compared fact: {fact}"));
        errors.AddRange(assignedFacts
            .GroupBy(static fact => fact, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => $"compared fact has {group.Count()} interaction families: {group.Key}"));

        var expectedPairs = new HashSet<string>(StringComparer.Ordinal);
        for (int earlier = 0; earlier < families.Count; earlier++)
        {
            for (int later = earlier + 1; later < families.Count; later++)
            {
                expectedPairs.Add(PairId(families[earlier].Id, families[later].Id));
            }
        }

        var actualPairs = new List<string>();
        foreach (ContractFactInteraction entry in entries)
        {
            if (!familyIds.Contains(entry.EarlierFamily, StringComparer.Ordinal) ||
                !familyIds.Contains(entry.LaterFamily, StringComparer.Ordinal))
            {
                errors.Add($"interaction names an unknown family: {entry.Id}");
                continue;
            }

            if (string.Equals(entry.EarlierFamily, entry.LaterFamily, StringComparison.Ordinal))
            {
                errors.Add($"interaction pairs a family with itself: {entry.Id}");
                continue;
            }

            string pair = PairId(entry.EarlierFamily, entry.LaterFamily);
            actualPairs.Add(pair);

            bool interacts = !string.IsNullOrWhiteSpace(entry.Rule) &&
                !string.IsNullOrWhiteSpace(entry.Witness) &&
                string.IsNullOrWhiteSpace(entry.NonInteractionReason);
            bool doesNotInteract = string.IsNullOrWhiteSpace(entry.Rule) &&
                string.IsNullOrWhiteSpace(entry.Witness) &&
                !string.IsNullOrWhiteSpace(entry.NonInteractionReason);
            if (interacts == doesNotInteract)
            {
                errors.Add($"family pair must have a witnessed rule or one non-interaction reason: {pair}");
            }

            if (interacts && entry.Witness is not null &&
                !entry.Witness.StartsWith("matrix:", StringComparison.Ordinal) &&
                !entry.Witness.StartsWith("test:", StringComparison.Ordinal))
            {
                errors.Add($"interacting family pair has an unbound witness: {pair}");
            }
        }

        errors.AddRange(expectedPairs.Except(actualPairs, StringComparer.Ordinal)
            .Select(static pair => $"fact-family interaction is unclassified: {pair}"));
        errors.AddRange(actualPairs.Except(expectedPairs, StringComparer.Ordinal)
            .Select(static pair => $"interaction is not an expected family pair: {pair}"));
        errors.AddRange(actualPairs
            .GroupBy(static pair => pair, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => $"fact-family interaction has {group.Count()} classifications: {group.Key}"));

        return errors;
    }

    private static ContractFactInteraction Interacts(
        string earlier,
        string later,
        string rule,
        string witness) =>
        new(earlier, later, rule, witness, null);

    private static ContractFactInteraction NonInteracting(
        string earlier,
        string later,
        string reason) =>
        new(earlier, later, null, null, reason);

    private static string PairId(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? $"{first} + {second}" : $"{second} + {first}";
}
