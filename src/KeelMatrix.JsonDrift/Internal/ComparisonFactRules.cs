using System.Reflection;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>One property populated by the metadata walk.</summary>
internal sealed record RecordedFactProperty(Type Owner, string Name)
{
    public string Id => $"{Owner.Name}.{Name}";
}

/// <summary>
/// The preservation rule for one kind of fact populated by the metadata walk. A fact is either compared under
/// a measured rule or explicitly non-contract-bearing; a comparison-rule gap always fails closed.
/// </summary>
internal sealed record ComparisonFactRule(
    string FactKind,
    IReadOnlyList<RecordedFactProperty> Properties,
    string? ComparedRule,
    string? Witness,
    string VerdictWhenRuleDoesNotApply,
    string? NonContractReason);

/// <summary>
/// The exhaustive bridge between facts populated by <see cref="MetadataTraversal"/> and the rules allowed to
/// support a compatibility verdict. <see cref="ValidateCoverage"/> reflects over every recorded fact type, so
/// adding a property without assigning it to this table fails the committed checks.
/// </summary>
internal static class ComparisonFactRules
{
    private const string Unsupported = "Unsupported";

    private static readonly Type[] RecordedTypes =
    {
        typeof(RecordedNode),
        typeof(RecordedMember),
        typeof(RecordedResolverFact),
        typeof(RecordedConverterFact),
        typeof(RecordedEdge),
        typeof(RecordedDerivedType),
        typeof(RecordedEnumMember),
        typeof(RecordedEnumWire),
        typeof(RecordedConstructorBinding),
        typeof(RecordedOptionSet),
        typeof(RecordedOptionValue),
        typeof(RecordedConverterConfiguration),
        typeof(RecordedAttributeFact),
        typeof(RecordedAttributeArgument),
    };

    public static IReadOnlyList<ComparisonFactRule> Entries { get; } = new ComparisonFactRule[]
    {
        Compared(
            "node JSON shape",
            "R07.shape.*",
            "matrix:R07.shape.list-to-dictionary, test:CollectionTransitionsRequireOrderingAndMultiplicityPreservation",
            Props<RecordedNode>(nameof(RecordedNode.Kind), nameof(RecordedNode.FrameworkKind))),
        Compared(
            "wire-bearing CLR identity",
            "R06.token-kind.*, R07.shape.dictionary-key-type",
            "matrix:R06.token-kind.numeric-widening, matrix:R07.shape.dictionary-key-type, test:SoundNumericWideningRemainsCompatible",
            Props<RecordedNode>(nameof(RecordedNode.TypeName))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.DeclaredType))).ToArray()),
        Compared(
            "value-slot null acceptance",
            "R05.nullability.value-nullable-*",
            "matrix:R05.nullability.value-nullable-removed, test:NullableValueAcceptanceIsRetainedAtEveryValueSlot",
            Props<RecordedNode>(nameof(RecordedNode.AcceptsNull))),
        Compared(
            "collection ordering and multiplicity",
            "R07.shape.collection-preservation",
            "test:CollectionTransitionsRequireOrderingAndMultiplicityPreservation",
            Props<RecordedNode>(nameof(RecordedNode.CollectionSemantics))),
        Compared(
            "dictionary materialization capability",
            "supported.dictionary, unsupported.dictionary-materialization-unproven",
            "test:DictionaryMaterializationInteractionsFailClosed",
            Props<RecordedNode>(nameof(RecordedNode.DictionaryMaterialization))),
        Compared(
            "object materialization capability",
            "supported.object, unsupported.object-materialization-unproven",
            "test:ConcreteObjectMaterializationMustBeSupportedByTheLaterReader",
            Props<RecordedNode>(nameof(RecordedNode.ObjectMaterialization))),
        Compared(
            "member serialized identity",
            "R02.property-*, R03.serialized-name.*",
            "matrix:R02.property-removal, matrix:R03.serialized-name.json-property-name",
            Props<RecordedMember>(nameof(RecordedMember.Name))),
        Compared(
            "member inclusion",
            "R09.ignore.*",
            "matrix:R09.ignore.member-included, test:IndependentMemberConstraintsAreReportedTogether",
            Props<RecordedMember>(nameof(RecordedMember.Included))),
        Compared(
            "member requiredness",
            "R04*.requiredness.*",
            "matrix:R04.requiredness.optional-to-required, test:IndependentMemberConstraintsAreReportedTogether",
            Props<RecordedMember>(nameof(RecordedMember.Required))),
        Compared(
            "member nullable annotations",
            "R05.nullability.reference-nullable-removed",
            "matrix:R05.nullability.reference-nullable-removed",
            Props<RecordedMember>(nameof(RecordedMember.GetNullable), nameof(RecordedMember.SetNullable))),
        Compared(
            "member materialization capability",
            "R02.property-materialization",
            "test:RemovingMemberMaterializationCapabilityDoesNotReportCompatible",
            Props<RecordedMember>(nameof(RecordedMember.CanSerialize), nameof(RecordedMember.CanDeserialize))),
        Compared(
            "extension-data capture",
            "R11*.extension-data.*",
            "matrix:R11.extension-data.removed, matrix:R11.extension-data.added",
            Props<RecordedMember>(nameof(RecordedMember.ExtensionData))),
        Compared(
            "constructor binding",
            "R12.binding.*",
            "matrix:R12.binding.constructor-parameter-added, test:IndependentMemberConstraintsAreReportedTogether",
            Props<RecordedMember>(nameof(RecordedMember.ConstructorBinding))
                .Concat(Props<RecordedConstructorBinding>(
                    nameof(RecordedConstructorBinding.Name),
                    nameof(RecordedConstructorBinding.Position),
                    nameof(RecordedConstructorBinding.HasDefaultValue))).ToArray()),
        Compared(
            "enum token domain",
            "R08.enum.*",
            "matrix:R08.enum.member-rename, test:EnumIntegerTokenAcceptanceIsComparedAtRootAndNestedSlots",
            Props<RecordedNode>(nameof(RecordedNode.EnumWire))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.EnumWire)))
                .Concat(Props<RecordedEnumWire>(
                    nameof(RecordedEnumWire.WritesStringTokens),
                    nameof(RecordedEnumWire.Unresolved),
                    nameof(RecordedEnumWire.ConverterConfiguration),
                    nameof(RecordedEnumWire.Members)))
                .Concat(Props<RecordedEnumMember>(
                    nameof(RecordedEnumMember.Name),
                    nameof(RecordedEnumMember.Token),
                    nameof(RecordedEnumMember.IsString)))
                .Concat(Props<RecordedConverterConfiguration>(
                    nameof(RecordedConverterConfiguration.ConverterType),
                    nameof(RecordedConverterConfiguration.IntegerTokensAccepted))).ToArray()),
        Compared(
            "serializer option set",
            "R03, R09, R13 and fail-closed option rules",
            "matrix:R13.source-generation.context-options",
            Props<RecordedNode>(nameof(RecordedNode.Options))
                .Concat(Props<RecordedOptionSet>(nameof(RecordedOptionSet.Values)))
                .Concat(Props<RecordedOptionValue>(nameof(RecordedOptionValue.Kind), nameof(RecordedOptionValue.Value))).ToArray()),
        Compared(
            "polymorphic derived-type contract",
            "R10*.polymorphism.*",
            "matrix:R10.polymorphism.discriminator-value-renamed, matrix:R10.polymorphism.derived-type-added",
            Props<RecordedNode>(
                nameof(RecordedNode.DerivedTypes),
                nameof(RecordedNode.DiscriminatorPropertyName),
                nameof(RecordedNode.UnknownDerivedTypeHandling))
                .Concat(Props<RecordedDerivedType>(
                    nameof(RecordedDerivedType.Discriminator),
                    nameof(RecordedDerivedType.TypeName),
                    nameof(RecordedDerivedType.Node))).ToArray()),
        Compared(
            "abstract/interface reader materialization",
            "R10d.polymorphism.discriminator-required, unsupported.object-materialization-unproven",
            "test:ConcreteToAbstractPolymorphicTransitionRequiresADiscriminator, test:PlainAbstractAndInterfaceReadersFailClosed",
            Props<RecordedNode>(nameof(RecordedNode.ReaderMaterializationRequiresDiscriminator))),
        Compared(
            "nested contract graph",
            "recursive application of this rule table",
            "matrix:D03.canonical-document.recursive-type, test:ReferenceOnlyDanglingAndAmbiguousGraphsAreRejectedBeforeComparison",
            Props<RecordedNode>(nameof(RecordedNode.Reference), nameof(RecordedNode.Members), nameof(RecordedNode.Edges))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.Shape)))
                .Concat(Props<RecordedEdge>(nameof(RecordedEdge.Node))).ToArray()),
        Compared(
            "metadata completeness",
            "unsupported.metadata-unavailable, unsupported.shape-evidence-missing, unsupported.traversal-budget",
            "matrix:R14.converter.traversal-depth-limit",
            Props<RecordedNode>(
                nameof(RecordedNode.MembersRecorded),
                nameof(RecordedNode.ConstructorsRecorded),
                nameof(RecordedNode.ElementTypeRecorded),
                nameof(RecordedNode.KeyTypeRecorded),
                nameof(RecordedNode.ValueTypeRecorded),
                nameof(RecordedNode.UnavailableReason))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.MetadataNotResolved))).ToArray()),
        Compared(
            "resolver classification",
            "supported.resolver.* and unsupported.resolver-*",
            "matrix:A01.adversarial.resolver-chain",
            Props<RecordedNode>(nameof(RecordedNode.Resolver))
                .Concat(Props<RecordedResolverFact>(
                    nameof(RecordedResolverFact.ResolverType),
                    nameof(RecordedResolverFact.ChainLength),
                    nameof(RecordedResolverFact.ModifierCount))).ToArray()),
        Compared(
            "converter classification",
            "supported.enum and unsupported.converter-*",
            "matrix:R14.converter.opaque-root",
            Props<RecordedNode>(nameof(RecordedNode.ConverterFacts))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.ConverterFacts)))
                .Concat(Props<RecordedConverterFact>(
                    nameof(RecordedConverterFact.TargetType),
                    nameof(RecordedConverterFact.ConverterType))).ToArray()),
        NonContract(
            "metadata provenance",
            "Paths and discovery-source labels explain where an effective fact came from; they do not change the JSON token domain.",
            Props<RecordedNode>(nameof(RecordedNode.Source), nameof(RecordedNode.Path))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.Path)))
                .Concat(Props<RecordedResolverFact>(nameof(RecordedResolverFact.Path)))
                .Concat(Props<RecordedConverterFact>(nameof(RecordedConverterFact.Source), nameof(RecordedConverterFact.Path)))
                .Concat(Props<RecordedEdge>(nameof(RecordedEdge.Source)))
                .Concat(Props<RecordedAttributeFact>(nameof(RecordedAttributeFact.Source), nameof(RecordedAttributeFact.Path))).ToArray()),
        NonContract(
            "runtime metadata handle",
            "The runtime Type handle is used only to obtain metadata; canonical type identity and effective wire facts are recorded separately.",
            Props<RecordedNode>(nameof(RecordedNode.Type))),
        NonContract(
            "declared attribute evidence",
            "Raw declarations are classification evidence; their effective name, inclusion, requiredness, nullability, extension-data, constructor, and polymorphism facts are recorded and compared separately.",
            Props<RecordedNode>(nameof(RecordedNode.DeclaredAttributes))
                .Concat(Props<RecordedMember>(nameof(RecordedMember.DeclaredAttributes)))
                .Concat(Props<RecordedAttributeFact>(
                    nameof(RecordedAttributeFact.AttributeType),
                    nameof(RecordedAttributeFact.Arguments)))
                .Concat(Props<RecordedAttributeArgument>(
                    nameof(RecordedAttributeArgument.Name),
                    nameof(RecordedAttributeArgument.Value))).ToArray()),
        NonContract(
            "derived display text",
            "Display and identifier helpers are deterministic renderings of separately catalogued facts and do not add contract information.",
            Props<RecordedOptionSet>(nameof(RecordedOptionSet.IsEmpty))
                .Concat(Props<RecordedOptionValue>(nameof(RecordedOptionValue.Id), nameof(RecordedOptionValue.Display)))
                .Concat(Props<RecordedConverterConfiguration>(nameof(RecordedConverterConfiguration.Display)))
                .Concat(Props<RecordedAttributeFact>(nameof(RecordedAttributeFact.Display))).ToArray()),
    };

    public static IReadOnlyList<string> ValidateCoverage()
    {
        var errors = new List<string>();
        RecordedFactProperty[] catalogued = Entries.SelectMany(static entry => entry.Properties).ToArray();
        string[] reflected = RecordedTypes
            .SelectMany(static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => $"{type.Name}.{property.Name}"))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string[] listed = catalogued.Select(static property => property.Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();

        errors.AddRange(reflected.Except(listed, StringComparer.Ordinal).Select(static id => $"recorded fact has no rule: {id}"));
        errors.AddRange(listed.Except(reflected, StringComparer.Ordinal).Select(static id => $"rule names no recorded fact: {id}"));
        errors.AddRange(listed.GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => $"recorded fact has {group.Count()} rules: {group.Key}"));

        foreach (ComparisonFactRule entry in Entries)
        {
            bool compared = !string.IsNullOrWhiteSpace(entry.ComparedRule);
            bool nonContract = !string.IsNullOrWhiteSpace(entry.NonContractReason);
            if (compared == nonContract)
            {
                errors.Add($"fact kind must be compared or non-contract-bearing, exclusively: {entry.FactKind}");
            }

            if (compared && string.IsNullOrWhiteSpace(entry.Witness))
            {
                errors.Add($"compared fact kind has no wire witness: {entry.FactKind}");
            }

            if (compared && entry.Witness is not null)
            {
                string[] witnesses = entry.Witness.Split(", ", StringSplitOptions.RemoveEmptyEntries);
                if (witnesses.Any(static witness =>
                    !witness.StartsWith("matrix:", StringComparison.Ordinal) &&
                    !witness.StartsWith("test:", StringComparison.Ordinal)))
                {
                    errors.Add($"compared fact kind has an unbound wire witness: {entry.FactKind}");
                }
            }

            if (compared && !string.Equals(entry.VerdictWhenRuleDoesNotApply, Unsupported, StringComparison.Ordinal))
            {
                errors.Add($"comparison rule gap does not fail closed: {entry.FactKind}");
            }
        }

        return errors;
    }

    private static ComparisonFactRule Compared(
        string factKind,
        string rule,
        string witness,
        IReadOnlyList<RecordedFactProperty> properties) =>
        new(factKind, properties, rule, witness, Unsupported, null);

    private static ComparisonFactRule NonContract(
        string factKind,
        string reason,
        IReadOnlyList<RecordedFactProperty> properties) =>
        new(factKind, properties, null, null, "Not applicable", reason);

    private static RecordedFactProperty[] Props<T>(params string[] names) =>
        names.Select(static name => new RecordedFactProperty(typeof(T), name)).ToArray();
}
