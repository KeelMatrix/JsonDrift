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
    private static readonly string[] ForbiddenNonInteractionFragments =
    {
        "canonical",
        "internal record",
        "separate field",
        "separate namespace",
        "checked independently",
        "facts are independent",
    };

    private static readonly string[] WireReasonTerms =
    {
        "JSON",
        "wire",
        "document",
        "reader",
        "writer",
        "token",
        "property",
        "key",
        "value",
        "member",
        "materializ",
        "constructor",
        "array",
        "object",
    };

    private static readonly string[] CausalReasonTerms =
    {
        "before",
        "after",
        "when",
        "while",
        "cannot",
        "never",
        "only",
        "so ",
    };

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
            "polymorphic discriminator, abstract/interface reader materialization, and derived-type contract",
            new[] { "polymorphic derived-type contract", "abstract/interface reader materialization" }),
        new(
            "object-materialization",
            "concrete object construction and reader materialization",
            new[] { "object materialization capability" }),
    };

    public static IReadOnlyList<ContractFactInteraction> Entries { get; } = new ContractFactInteraction[]
    {
        Interacts("member-identity", "member-inclusion", "R03 serialized identity is evaluated only for effective members; R09 inclusion is still reported independently", "test:IndependentMemberConstraintsAreReportedTogether"),
        Interacts("member-identity", "requiredness", "a rename destination must satisfy R04 requiredness before R03 extension-data preservation can be accepted", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        NonInteracting("member-identity", "nullability", "a JSON property name selects a member before its value is read; changing the selected name cannot change whether a null token is accepted by a member that was selected, and a renamed unselected value is already rejected by R03"),
        NonInteracting("member-identity", "member-materialization", "a JSON property name selects a member before assignment; a setter or constructor binding cannot alias a different wire name, and R03 already rejects a written key that no longer selects the destination member"),
        Interacts("member-identity", "token-domain", "rename matching requires the same recorded token domain; otherwise the change is removal plus addition and fails closed", "matrix:R03.serialized-name.json-property-name"),
        Interacts("member-identity", "container-shape", "rename matching requires the same declared container shape before R03 can apply", "matrix:R03.serialized-name.json-property-name"),
        NonInteracting("member-identity", "container-materialization", "a JSON property name selects the destination before an array or object value is materialized; a container constructor cannot make a differently named wire property select that destination"),
        NonInteracting("member-identity", "enum-domain", "enum tokens are compared after a member is selected; enum metadata cannot select or alias a JSON property name"),
        Interacts("member-identity", "extension-data", "R03 allows a rename only when the old key is preserved by later extension data and the earlier extension key space cannot shadow the destination", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("member-identity", "reference-graph", "serialized identity is reapplied at every recursively reached object node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("member-identity", "serializer-configuration", "property naming options participate in R03 and unmeasured resolver or modifier changes fail closed", "matrix:R03.serialized-name.naming-policy"),
        Interacts("member-identity", "polymorphism", "a discriminator is a JSON property name across its entire reachable registered hierarchy, so a colliding effective ordinary member on the declaring contract or a derived contract can be reinterpreted as dispatch metadata and must fail closed", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        Interacts("member-inclusion", "requiredness", "an included destination still has its independent R04 requiredness checked after R09", "test:IndependentMemberConstraintsAreReportedTogether"),
        NonInteracting("member-inclusion", "nullability", "an excluded member writes and binds no JSON property value, while an included member applies null acceptance only after its property is present; neither rule can make a missing value satisfy the other"),
        Interacts("member-inclusion", "member-materialization", "an included member must retain materialization even when R09 also changes", "test:IndependentMemberConstraintsAreReportedTogether"),
        NonInteracting("member-inclusion", "token-domain", "an excluded member writes and binds no JSON value token, while an included member checks its scalar token domain only after the property is present; token acceptance cannot include an omitted member"),
        NonInteracting("member-inclusion", "container-shape", "an excluded member writes and binds no JSON value, while an included member checks array or object shape only after the property is present; a container shape cannot include an omitted member"),
        NonInteracting("member-inclusion", "container-materialization", "an excluded member never invokes a container reader, while an included member materializes its array or object only after the property is present; a materializer cannot include an omitted member"),
        NonInteracting("member-inclusion", "enum-domain", "an excluded member writes and binds no JSON enum token, while an included member checks named or integer enum values only after the property is present; an enum value cannot include an omitted member"),
        Interacts("member-inclusion", "extension-data", "only an effective named member can shadow an earlier extension-data key; ignored additions leave the key in extension data", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("member-inclusion", "reference-graph", "R09 inclusion is evaluated independently at every recursively reached object node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("member-inclusion", "serializer-configuration", "ignore options and attributes decide effective inclusion and unmeasured values fail closed", "matrix:R09.ignore.condition-when-writing-null"),
        Interacts("member-inclusion", "polymorphism", "only an effective ordinary JSON member can collide with the discriminator property; ignoring or including that member changes whether dispatch metadata and member data occupy the same wire name", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        Interacts("requiredness", "nullability", "R04 distinguishes missing members from explicit null tokens, while R05 independently checks whether null can be read", "matrix:R04.requiredness.explicit-null"),
        Interacts("requiredness", "member-materialization", "constructor-bound additions retain both their presence requirement and their materialization constraint", "test:IndependentMemberConstraintsAreReportedTogether"),
        NonInteracting("requiredness", "token-domain", "requiredness rejects a document only when the JSON property is missing, while scalar token-domain validation runs only when a value is present; neither check can turn absence into an accepted token"),
        NonInteracting("requiredness", "container-shape", "requiredness rejects a document only when the JSON property is missing, while array or object shape is checked only for a present value; container shape cannot satisfy an absent required property"),
        NonInteracting("requiredness", "container-materialization", "requiredness rejects a document before any missing JSON value can reach a container reader, while materialization applies only to a present array or object; construction cannot satisfy absence"),
        NonInteracting("requiredness", "enum-domain", "requiredness rejects a document only when the JSON property is missing, while named or integer enum validation applies only to a present token; an enum value cannot satisfy absence"),
        Interacts("requiredness", "extension-data", "capturing an old key as extension data cannot satisfy a differently named required destination member", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("requiredness", "reference-graph", "R04 is evaluated at every recursively reached object node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("requiredness", "serializer-configuration", "required-constructor enforcement is an option-dependent presence constraint", "matrix:R12.binding.constructor-parameter-added.enforced"),
        Interacts("requiredness", "polymorphism", "a required ordinary member can occupy the discriminator JSON property name; the reader may consume that one property for dispatch instead of satisfying the member requirement, so the collision fails closed", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        NonInteracting("nullability", "member-materialization", "a nullable member still permits non-null JSON values, so accepting null cannot prove that a reader lacking a setter or constructor binding preserves the member's full wire domain; each failure remains independently decisive"),
        Interacts("nullability", "token-domain", "R05 null-token acceptance is checked before the non-null R06 token domain", "test:NullableValueAcceptanceIsRetainedAtEveryValueSlot"),
        NonInteracting("nullability", "container-shape", "a JSON null token is handled at the value slot before any non-null array or object shape is selected, so accepting null cannot make an incompatible container token readable"),
        NonInteracting("nullability", "container-materialization", "a JSON null token is accepted or rejected before an array or object materializer is invoked, so null acceptance cannot repair a container reader that cannot construct its non-null value"),
        NonInteracting("nullability", "enum-domain", "a JSON null token is accepted or rejected before named-string or integer enum tokens are interpreted, so null acceptance cannot add or remove a non-null enum wire value"),
        Interacts("nullability", "extension-data", "an earlier extension-data value can be null while a later named non-nullable member shadows that key and rejects the token", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("nullability", "reference-graph", "null acceptance is retained on every edge before reference resolution", "test:NullableValueAcceptanceIsRetainedAtEveryValueSlot"),
        Interacts("nullability", "serializer-configuration", "nullable-annotation enforcement is recorded from serializer options and compared by R05/R13", "matrix:R05.nullability.reference-nullable-removed.enforced"),
        Interacts("nullability", "polymorphism", "a nullable ordinary member can write null at the discriminator JSON property name; that token is consumed as dispatch metadata rather than as the member value, so the name collision fails closed", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        NonInteracting("member-materialization", "token-domain", "after a JSON property selects a member, every accepted scalar token still requires the same setter or constructor binding; changing the scalar domain cannot restore a value when assignment is unavailable"),
        NonInteracting("member-materialization", "container-shape", "after a JSON property selects a member, its setter or constructor binding assigns the selected value but cannot turn an array token into an object token or otherwise change the container wire shape"),
        Interacts("member-materialization", "container-materialization", "a writable member is preservable only when its selected collection or dictionary can also be materialized", "test:DictionaryMaterializationInteractionsFailClosed"),
        NonInteracting("member-materialization", "enum-domain", "after a JSON property selects an enum member, every accepted named or integer token still requires the same setter or constructor binding; enum acceptance cannot restore an unassignable value"),
        Interacts("member-materialization", "extension-data", "R03 extension-data preservation is usable only when the capture member itself has a measured materialization path", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("member-materialization", "reference-graph", "member materialization is checked at every recursively reached object node", "test:RemovingMemberMaterializationCapabilityDoesNotReportCompatible"),
        Interacts("member-materialization", "serializer-configuration", "constructor and populate options participate in the effective materialization path and unmeasured values fail closed", "matrix:A01.adversarial.options-preferred-object-creation-handling"),
        Interacts("member-materialization", "polymorphism", "when an ordinary member shares the discriminator JSON name, dispatch consumes the property before ordinary member assignment; setter or constructor-binding evidence cannot prove the colliding value is preserved", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        Interacts("token-domain", "container-shape", "R06 scalar-token changes and R07 container-shape changes share the same node dispatch and neither can mask the other", "matrix:R06.token-kind.public-comparison"),
        NonInteracting("token-domain", "container-materialization", "a scalar JSON token never invokes an array or object container materializer, and a present array or object token is rejected as a scalar before construction; a shape transition is already classified by R07"),
        Interacts("token-domain", "enum-domain", "R08 records whether the enum accepts integer or named string tokens before R06 compares its token kind", "matrix:R08.enum.number-to-string.tokens-only"),
        Interacts("token-domain", "extension-data", "an earlier extension-data value can carry any token kind and a later named member may bind and reject it", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("token-domain", "reference-graph", "R06 token domains are compared at roots and every recursively reached value node", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("token-domain", "serializer-configuration", "number handling and converters can change accepted token domains; unmeasured configurations fail closed", "matrix:A01.adversarial.options-number-handling"),
        Interacts("token-domain", "polymorphism", "an ordinary scalar member can share both the discriminator JSON property name and its token position; dispatch can reinterpret or reject that member token before ordinary scalar binding", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        Interacts("container-shape", "container-materialization", "R07 support requires both matching key/value or element shape and a measured later materializer", "test:DictionaryMaterializationInteractionsFailClosed"),
        NonInteracting("container-shape", "enum-domain", "JSON arrays and objects cannot be enum number or string tokens at the same value position; a transition between those token kinds is rejected by R07 before an enum value-domain rule could apply"),
        Interacts("container-shape", "extension-data", "an earlier extension-data value can carry object or array shape while a later named scalar member shadows that key and rejects it", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("container-shape", "reference-graph", "collection elements and dictionary key/value nodes are recursively traversed and compared", "matrix:R07.shape.nested-contract.public-comparison"),
        Interacts("container-shape", "serializer-configuration", "dictionary-key policy and converters can change container wire shape or key space and unmeasured values fail closed", "matrix:A01.adversarial.options-dictionary-key-policy"),
        Interacts("container-shape", "polymorphism", "registered derived types can themselves be collections or dictionaries and must be traversed as those shapes", "matrix:A01.adversarial.derived-dictionary"),

        NonInteracting("container-materialization", "enum-domain", "an enum number or string token never invokes an array or object materializer, and a present container token is rejected as an enum before construction; neither reader decision can mask the other"),
        Interacts("container-materialization", "extension-data", "R03 extension-data preservation requires the capture dictionary to pass the ordinary dictionary materialization gate", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("container-materialization", "reference-graph", "materialization support is required at root and nested container nodes reached through members", "test:DictionaryMaterializationInteractionsFailClosed"),
        Interacts("container-materialization", "serializer-configuration", "object-creation handling can select a different container population path and unmeasured values fail closed", "matrix:A01.adversarial.options-preferred-object-creation-handling"),
        Interacts("container-materialization", "polymorphism", "a collection- or dictionary-shaped derived type must pass its own materialization gate after discriminator dispatch", "matrix:A01.adversarial.derived-dictionary"),

        Interacts("enum-domain", "extension-data", "an earlier extension-data key can carry a token outside a later named enum member's accepted domain", "test:ExtensionDataKeyCollisionsFailClosedAcrossTokenKinds"),
        Interacts("enum-domain", "reference-graph", "enum named and integer domains are recorded at roots and nested value slots", "test:EnumIntegerTokenAcceptanceIsComparedAtRootAndNestedSlots"),
        Interacts("enum-domain", "serializer-configuration", "enum converters and their integer-token setting define the recorded R08 domain", "matrix:R08.enum.converter-configuration"),
        Interacts("enum-domain", "polymorphism", "an ordinary enum member can occupy the discriminator JSON property name and write a string or number there; dispatch consumes that token under the discriminator domain instead of the enum domain", "test:DiscriminatorMemberNameCollisionsFailClosed"),

        Interacts("extension-data", "reference-graph", "extension-data key/value contracts are traversed recursively and their unsupported descendants fail the whole contract", "matrix:R14.converter.opaque-extension-data-value"),
        Interacts("extension-data", "serializer-configuration", "unmapped-member handling and resolver changes can alter capture behavior, so unmeasured settings fail closed", "matrix:A01.adversarial.options-unmapped-member-handling"),
        Interacts("extension-data", "polymorphism", "adding polymorphic dispatch can reinterpret an earlier extension-data key as the discriminator and reject an unrecognized value", "test:ExtensionDataUnknownDiscriminatorFailsClosedWhenPolymorphismIsAdded"),

        Interacts("reference-graph", "serializer-configuration", "resolver, converter, and option gates are applied to every recursively reached node", "matrix:R14.converter.opaque-object-graph-depth-2"),
        Interacts("reference-graph", "polymorphism", "registered derived types and nested polymorphic bases are part of the bounded recursive graph", "matrix:A01.adversarial.derived-polymorphic-base"),

        Interacts("serializer-configuration", "polymorphism", "resolver modifiers and converters can rewrite derived metadata, so only measured configuration is allowed", "matrix:A01.adversarial.type-info-modifier"),

        Interacts("member-identity", "object-materialization", "parameterized object construction binds JSON properties through effective members, so serialized identity and the selected constructor jointly decide whether an earlier value reaches the later object", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("member-inclusion", "object-materialization", "only included JSON members can supply values to a parameterized object constructor, so inclusion and the selected construction path jointly decide whether the reader can create the object", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("requiredness", "object-materialization", "constructor parameters can impose presence constraints during object construction, so missing-property handling and the selected materializer jointly decide whether the later reader accepts a document", "test:IndependentMemberConstraintsAreReportedTogether"),
        Interacts("nullability", "object-materialization", "constructor-bound JSON values must satisfy null acceptance before the selected object constructor can run, so both facts participate in reader materialization", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("member-materialization", "object-materialization", "the reader must first construct the containing object and then assign or constructor-bind each JSON member; either missing materialization path loses the earlier value", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("token-domain", "object-materialization", "constructor-bound JSON tokens must be accepted by their parameter types before the selected object construction path can run, so both facts decide reader success", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("container-shape", "object-materialization", "an object-shaped JSON value requires a supported object constructor, while transitions to or from arrays and dictionaries change the materializer selected for the same value position", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("container-materialization", "object-materialization", "nested object and container values each require their measured construction path; a containing object cannot preserve a document when either its constructor or a child container materializer is unsupported", "test:DictionaryMaterializationInteractionsFailClosed"),
        Interacts("enum-domain", "object-materialization", "constructor-bound enum tokens must be accepted by the enum domain before the selected object constructor can run, so both facts decide whether the reader materializes the object", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("extension-data", "object-materialization", "an extension-data dictionary is populated on an object only after the containing reader selects a supported construction path; failure of either materializer prevents preservation", "test:RequiredRenameWithExtensionDataDoesNotBypassDestinationConstraints"),
        Interacts("reference-graph", "object-materialization", "object construction must be supported at the root and every recursively reached member node, including repeated types reached through references", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("serializer-configuration", "object-materialization", "resolver and converter configuration can replace object creation behavior, so only construction paths measured under the allowlisted metadata configuration are supported", "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader"),
        Interacts("polymorphism", "object-materialization", "a concrete object constructor and discriminator-based derived-type construction are alternative reader materialization paths, and the applicable path determines whether an earlier document can be instantiated", "test:ConcreteToAbstractPolymorphicTransitionRequiresADiscriminator"),
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

            if (doesNotInteract && !IsWireSpecificNonInteractionReason(entry.NonInteractionReason!))
            {
                errors.Add($"non-interaction reason is not wire-specific: {pair}");
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

    private static bool IsWireSpecificNonInteractionReason(string reason)
    {
        if (reason.Length < 80)
        {
            return false;
        }

        if (ForbiddenNonInteractionFragments.Any(fragment => reason.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        int wireTermCount = WireReasonTerms.Count(term => reason.Contains(term, StringComparison.OrdinalIgnoreCase));
        bool explainsCausality = CausalReasonTerms
            .Any(term => reason.Contains(term, StringComparison.OrdinalIgnoreCase));

        return wireTermCount >= 2 && explainsCausality;
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
