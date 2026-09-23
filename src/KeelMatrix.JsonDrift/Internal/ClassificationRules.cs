using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// The documented identifiers of every classification rule the deny-by-default classifier can apply. A
/// verdict always names one of these identifiers, and the documentation check requires the catalogued
/// identifiers to be the documented ones.
/// </summary>
internal static class RuleIds
{
    public const string SupportedResolverDefaultReflection = "supported.resolver.default-reflection";
    public const string SupportedResolverSourceGenerated = "supported.resolver.source-generated";
    public const string SupportedObject = "supported.object";
    public const string SupportedEnumerable = "supported.enumerable";
    public const string SupportedDictionary = "supported.dictionary";
    public const string SupportedScalar = "supported.scalar";
    public const string SupportedEnum = "supported.enum";
    public const string SupportedMember = "supported.member";
    public const string SupportedReference = "supported.reference";

    public const string UnsupportedMetadataUnavailable = "unsupported.metadata-unavailable";
    public const string UnsupportedTraversalBudget = "unsupported.traversal-budget";
    public const string UnsupportedResolverUnrecognized = "unsupported.resolver-unrecognized";
    public const string UnsupportedResolverChain = "unsupported.resolver-chain";
    public const string UnsupportedResolverModifiers = "unsupported.resolver-modifiers";
    public const string UnsupportedConverterUnrecognized = "unsupported.converter-unrecognized";
    public const string UnsupportedConverterUnlisted = "unsupported.converter-unlisted";
    public const string UnsupportedShapeEvidenceMissing = "unsupported.shape-evidence-missing";
    public const string UnsupportedScalarUnlisted = "unsupported.scalar-unlisted";
    public const string UnsupportedEnumWireUnresolved = "unsupported.enum-wire-unresolved";
    public const string UnsupportedOptionUnlisted = "unsupported.option-unlisted";
    public const string UnsupportedAttributeUnlisted = "unsupported.attribute-unlisted";
    public const string UnsupportedConverterConfigurationUnlisted = "unsupported.converter-configuration-unlisted";
    public const string UnsupportedCollectionSemanticsUnproven = "unsupported.collection-semantics-unproven";
    public const string UnsupportedDictionaryMaterializationUnproven = "unsupported.dictionary-materialization-unproven";
    public const string UnsupportedObjectMaterializationUnproven = "unsupported.object-materialization-unproven";
    public const string UnsupportedPolymorphismDiscriminatorMemberCollision = "unsupported.polymorphism-discriminator-member-collision";
    public const string UnsupportedMemberMaterializationUnproven = "unsupported.member-materialization-unproven";
}

/// <summary>One documented classification rule.</summary>
internal sealed record ClassificationRule(string Id, bool Supported, string Summary);

/// <summary>
/// The classification rule catalogue. A supported verdict is only reachable through a
/// <c>supported.*</c> rule of this catalogue, and the rule-table check requires the catalogue to be exactly
/// the table published in <c>docs/compatibility-rules.md</c>, so a verdict cannot exist without a
/// documented rule.
/// </summary>
internal static class RuleCatalog
{
    public static IReadOnlyList<ClassificationRule> Entries { get; } = new ClassificationRule[]
    {
        new(
            RuleIds.SupportedResolverDefaultReflection,
            true,
            "the metadata came from a single DefaultJsonTypeInfoResolver without modifiers"),
        new(
            RuleIds.SupportedResolverSourceGenerated,
            true,
            "the metadata came from a single source-generated JsonSerializerContext"),
        new(
            RuleIds.SupportedObject,
            true,
            "an object contract whose members, constructor parameters, and registered derived types were all recorded and are classifiable"),
        new(
            RuleIds.SupportedEnumerable,
            true,
            "an enumerable contract whose element type was recorded and is classifiable"),
        new(
            RuleIds.SupportedDictionary,
            true,
            "a constructible dictionary contract whose key and value types were recorded and are classifiable"),
        new(
            RuleIds.SupportedScalar,
            true,
            "a framework scalar type on the scalar allowlist"),
        new(
            RuleIds.SupportedEnum,
            true,
            "an enum type whose wire identity was produced and whose effective converters are allowlisted"),
        new(
            RuleIds.SupportedMember,
            true,
            "a member whose converters are allowlisted, whose recorded shape is classifiable, and whose enum wire identity was produced"),
        new(
            RuleIds.SupportedReference,
            true,
            "a repeated visit of a type that was already recorded and is classifiable"),
        new(
            RuleIds.UnsupportedMetadataUnavailable,
            false,
            "the framework could not produce metadata for the type with these options"),
        new(
            RuleIds.UnsupportedTraversalBudget,
            false,
            "the contract nests deeper than the traversal budget, so its metadata was not recorded"),
        new(
            RuleIds.UnsupportedResolverUnrecognized,
            false,
            "the metadata resolver is not a recognized framework metadata source"),
        new(
            RuleIds.UnsupportedResolverChain,
            false,
            "the options resolve metadata through more than one resolver"),
        new(
            RuleIds.UnsupportedResolverModifiers,
            false,
            "the default reflection resolver carries JsonTypeInfo modifiers that can rewrite metadata"),
        new(
            RuleIds.UnsupportedConverterUnrecognized,
            false,
            "a declared or registered converter does not ship in the framework System.Text.Json assembly"),
        new(
            RuleIds.UnsupportedConverterUnlisted,
            false,
            "a declared or registered converter ships in the framework assembly but is not on the converter allowlist"),
        new(
            RuleIds.UnsupportedShapeEvidenceMissing,
            false,
            "the recorded shape of the contract has no recorded element, key, value, member, or constructor metadata"),
        new(
            RuleIds.UnsupportedScalarUnlisted,
            false,
            "the recorded scalar type is not on the scalar allowlist"),
        new(
            RuleIds.UnsupportedEnumWireUnresolved,
            false,
            "the wire name of an enum member could not be produced from the recorded framework converter"),
        new(
            RuleIds.UnsupportedOptionUnlisted,
            false,
            "a recorded serializer option value is not on the option allowlist the committed checks are measured under"),
        new(
            RuleIds.UnsupportedAttributeUnlisted,
            false,
            "a declared JSON serialization attribute or argument value is not on the attribute allowlist the committed checks are measured under"),
        new(
            RuleIds.UnsupportedConverterConfigurationUnlisted,
            false,
            "an allowlisted framework enum converter produced an observable configuration outside the measured converter-configuration allowlist"),
        new(
            RuleIds.UnsupportedCollectionSemanticsUnproven,
            false,
            "the enumerable materializer has no measured witness that it preserves item order and multiplicity"),
        new(
            RuleIds.UnsupportedDictionaryMaterializationUnproven,
            false,
            "the dictionary materializer has no measured witness that the framework reader can construct and populate it"),
        new(
            RuleIds.UnsupportedObjectMaterializationUnproven,
            false,
            "an object contract has no measured concrete construction path or requires polymorphic dispatch without registered derived-type metadata"),
        new(
            RuleIds.UnsupportedPolymorphismDiscriminatorMemberCollision,
            false,
            "a polymorphic discriminator property name collides with an effective ordinary serialized member name"),
        new(
            RuleIds.UnsupportedMemberMaterializationUnproven,
            false,
            "a serialized member has no structural setter or constructor binding that can materialize its value"),
    };

    public static bool Contains(string ruleId) =>
        Entries.Any(entry => string.Equals(entry.Id, ruleId, StringComparison.Ordinal));

    public static bool IsSupportedRule(string ruleId) =>
        Entries.Any(entry => entry.Supported && string.Equals(entry.Id, ruleId, StringComparison.Ordinal));
}

/// <summary>
/// The classification rules a recorded node kind can reach. Every switch is exhaustive, so a node kind
/// added without a rule fails the Release build, and the coverage check requires the rules of every visited
/// node kind to be documented.
/// </summary>
internal static class RecordedKindRules
{
    public static readonly RecordedNodeKind[] All = Enum.GetValues<RecordedNodeKind>();

    public static string Name(RecordedNodeKind kind) => kind switch
    {
        RecordedNodeKind.Object => "object",
        RecordedNodeKind.Enumerable => "enumerable",
        RecordedNodeKind.Dictionary => "dictionary",
        RecordedNodeKind.Scalar => "scalar",
        RecordedNodeKind.Reference => "reference",
        RecordedNodeKind.Unavailable => "unavailable",
    };

    /// <summary>Every rule that can classify a node or member of the kind.</summary>
    public static IReadOnlyList<string> ClassificationRules(RecordedNodeKind kind) => kind switch
    {
        RecordedNodeKind.Object => new[]
        {
            RuleIds.SupportedObject,
            RuleIds.SupportedMember,
            RuleIds.SupportedReference,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedMetadataUnavailable,
            RuleIds.UnsupportedTraversalBudget,
            RuleIds.UnsupportedOptionUnlisted,
            RuleIds.UnsupportedAttributeUnlisted,
            RuleIds.UnsupportedConverterConfigurationUnlisted,
            RuleIds.UnsupportedObjectMaterializationUnproven,
            RuleIds.UnsupportedPolymorphismDiscriminatorMemberCollision,
            RuleIds.UnsupportedMemberMaterializationUnproven,
        },
        RecordedNodeKind.Enumerable => new[]
        {
            RuleIds.SupportedEnumerable,
            RuleIds.SupportedReference,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedMetadataUnavailable,
            RuleIds.UnsupportedTraversalBudget,
            RuleIds.UnsupportedOptionUnlisted,
            RuleIds.UnsupportedAttributeUnlisted,
            RuleIds.UnsupportedConverterConfigurationUnlisted,
            RuleIds.UnsupportedCollectionSemanticsUnproven,
        },
        RecordedNodeKind.Dictionary => new[]
        {
            RuleIds.SupportedDictionary,
            RuleIds.SupportedReference,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedMetadataUnavailable,
            RuleIds.UnsupportedTraversalBudget,
            RuleIds.UnsupportedOptionUnlisted,
            RuleIds.UnsupportedAttributeUnlisted,
            RuleIds.UnsupportedConverterConfigurationUnlisted,
            RuleIds.UnsupportedDictionaryMaterializationUnproven,
        },
        RecordedNodeKind.Scalar => new[]
        {
            RuleIds.SupportedScalar,
            RuleIds.SupportedEnum,
            RuleIds.SupportedReference,
            RuleIds.UnsupportedScalarUnlisted,
            RuleIds.UnsupportedEnumWireUnresolved,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedOptionUnlisted,
            RuleIds.UnsupportedAttributeUnlisted,
            RuleIds.UnsupportedConverterConfigurationUnlisted,
        },
        RecordedNodeKind.Reference => new[]
        {
            RuleIds.SupportedReference,
            RuleIds.UnsupportedMetadataUnavailable,
            RuleIds.UnsupportedAttributeUnlisted,
            RuleIds.UnsupportedConverterConfigurationUnlisted,
        },
        RecordedNodeKind.Unavailable => new[]
        {
            RuleIds.UnsupportedTraversalBudget,
            RuleIds.UnsupportedMetadataUnavailable,
        },
    };
}

/// <summary>One allowlisted framework converter.</summary>
internal sealed record ConverterAllowlistEntry(string DisplayName, Type Definition, bool EnumTargetOnly);

/// <summary>
/// The positive allowlists of the deny-by-default classifier: the framework scalar types, the framework
/// converters, and the framework metadata resolvers that a supported verdict may rest on. Everything else
/// is reported unsupported, so a construct that is not on an allowlist can never produce a green result.
/// The documentation check binds every allowlist to the list published in <c>docs/compatibility-rules.md</c>.
/// </summary>
internal static class ContractAllowlists
{
    private static readonly Type FrameworkAssemblyAnchor = typeof(JsonSerializer);

    /// <summary>The framework scalar types whose JSON representation the allowlist recognizes.</summary>
    public static IReadOnlyList<Type> ScalarTypes { get; } = new[]
    {
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(char),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(Int128),
        typeof(UInt128),
        typeof(Half),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(string),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
        typeof(Uri),
        typeof(Version),
        typeof(byte[]),
        typeof(Memory<byte>),
        typeof(ReadOnlyMemory<byte>),
        typeof(object),
        typeof(JsonElement),
        typeof(JsonDocument),
        typeof(System.Text.Json.Nodes.JsonNode),
    };

    /// <summary>The framework converters the allowlist recognizes.</summary>
    public static IReadOnlyList<ConverterAllowlistEntry> Converters { get; } = new[]
    {
        new ConverterAllowlistEntry(
            "System.Text.Json.Serialization.JsonStringEnumConverter",
            typeof(JsonStringEnumConverter),
            EnumTargetOnly: true),
        new ConverterAllowlistEntry(
            "System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>",
            typeof(JsonStringEnumConverter<>),
            EnumTargetOnly: true),
        new ConverterAllowlistEntry(
            "System.Text.Json.Serialization.JsonNumberEnumConverter<TEnum>",
            typeof(JsonNumberEnumConverter<>),
            EnumTargetOnly: true),
    };

    /// <summary>The framework metadata resolvers the allowlist recognizes.</summary>
    public static IReadOnlyList<Type> Resolvers { get; } = new[]
    {
        typeof(DefaultJsonTypeInfoResolver),
        typeof(JsonSerializerContext),
    };

    /// <summary>The recorded scalar type names the documentation check compares with the published list.</summary>
    public static IReadOnlyList<string> ScalarTypeNames { get; } =
        ScalarTypes.Select(TypeShapes.TypeName).ToArray();

    /// <summary>The recorded converter names the documentation check compares with the published list.</summary>
    public static IReadOnlyList<string> ConverterNames { get; } =
        Converters.Select(static entry => entry.DisplayName).ToArray();

    /// <summary>The recorded resolver names the documentation check compares with the published list.</summary>
    public static IReadOnlyList<string> ResolverNames { get; } =
        Resolvers.Select(TypeShapes.TypeName).ToArray();

    /// <summary>
    /// The recorded serializer option values a supported verdict may rest on, derived from the option
    /// profiles the committed checks are measured under (<see cref="JsonContractOptions.MeasuredOptionValues"/>)
    /// rather than written out a second time. Every other value of every family is reported through
    /// <c>unsupported.option-unlisted</c>.
    /// </summary>
    public static IReadOnlyList<RecordedOptionValue> OptionValues { get; } =
        JsonContractOptions.MeasuredOptionValues;

    /// <summary>The recorded option-value names the documentation check compares with the published list.</summary>
    public static IReadOnlyList<string> OptionValueNames { get; } =
        OptionValues.Select(static value => value.Display).ToArray();

    /// <summary>
    /// The declared JSON attribute facts accepted by the classifier, derived from the measured contract
    /// surfaces rather than copied into a second list.
    /// </summary>
    public static IReadOnlyList<RecordedAttributeFact> AttributeValues { get; } =
        DeclaredAttributeFacts.MeasuredValues;

    /// <summary>The stable declared-attribute names compared with the documentation.</summary>
    public static IReadOnlyList<string> AttributeValueNames { get; } =
        AttributeValues.Select(static value => value.Display).ToArray();

    /// <summary>
    /// The observable enum converter configurations accepted by the matrix, derived from the committed
    /// converter profiles rather than from converter private state.
    /// </summary>
    public static IReadOnlyList<RecordedConverterConfiguration> ConverterConfigurations { get; } =
        JsonContractOptions.MeasuredConverterConfigurations;

    /// <summary>The stable converter-configuration values compared with the documentation.</summary>
    public static IReadOnlyList<string> ConverterConfigurationNames { get; } =
        ConverterConfigurations.Select(static value => value.Display).ToArray();

    /// <summary>Whether the type ships in the framework <c>System.Text.Json</c> assembly.</summary>
    public static bool IsFrameworkAssembly(Type type) => type.Assembly == FrameworkAssemblyAnchor.Assembly;

    /// <summary>Whether a scalar type is on the allowlist.</summary>
    public static bool IsAllowlistedScalar(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return ScalarTypes.Contains(Nullable.GetUnderlyingType(type) ?? type);
    }

    /// <summary>
    /// Whether a recorded serializer option value is one the committed checks are measured under. The value
    /// is compared as recorded, so a family whose value the allowlist does not name can never produce a
    /// supported verdict.
    /// </summary>
    public static bool IsAllowlistedOptionValue(ContractOptionKind kind, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return OptionValues.Any(entry =>
            entry.Kind == kind && string.Equals(entry.Value, value, StringComparison.Ordinal));
    }

    /// <summary>Whether a reflection-declared JSON attribute fact is on the measured allowlist.</summary>
    public static bool IsAllowlistedAttribute(RecordedAttributeFact attribute) =>
        AttributeValues.Any(allowlisted => string.Equals(allowlisted.Display, attribute.Display, StringComparison.Ordinal));

    /// <summary>Whether a probed enum converter configuration is on the measured allowlist.</summary>
    public static bool IsAllowlistedConverterConfiguration(RecordedConverterConfiguration configuration) =>
        ConverterConfigurations.Contains(configuration);

    /// <summary>
    /// Whether a converter type is a framework converter on the allowlist. Converter provenance is decided by
    /// assembly identity, not by namespace: an application converter that declares a
    /// <c>System.Text.Json</c> namespace is still an unrecognized converter.
    /// </summary>
    public static bool IsAllowlistedConverterType(Type converterType)
    {
        ArgumentNullException.ThrowIfNull(converterType);

        if (!IsFrameworkAssembly(converterType))
        {
            return false;
        }

        Type definition = converterType.IsGenericType ? converterType.GetGenericTypeDefinition() : converterType;

        return Converters.Any(entry => entry.Definition == definition || entry.Definition == converterType);
    }

    /// <summary>
    /// Whether a converter on the allowlist may be applied to the recorded target type. An enum converter is
    /// only recognized when the target is an enum, and a generic enum converter is only recognized when its
    /// type argument is the target.
    /// </summary>
    public static bool IsAllowlistedConverterFor(Type converterType, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(converterType);
        ArgumentNullException.ThrowIfNull(targetType);

        if (!IsAllowlistedConverterType(converterType))
        {
            return false;
        }

        Type target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        ConverterAllowlistEntry? entry = Converters.FirstOrDefault(candidate =>
            candidate.Definition == converterType ||
            (converterType.IsGenericType && candidate.Definition == converterType.GetGenericTypeDefinition()));

        if (entry is null)
        {
            return false;
        }

        if (entry.EnumTargetOnly && !target.IsEnum)
        {
            return false;
        }

        if (!converterType.IsGenericType)
        {
            return true;
        }

        Type argument = converterType.GetGenericArguments()[0];
        return (Nullable.GetUnderlyingType(argument) ?? argument) == target;
    }

    /// <summary>
    /// Whether the recorded resolver is a recognized metadata source: exactly one resolver, no modifiers, and
    /// either the default reflection resolver or a source-generated serializer context.
    /// </summary>
    public static bool IsAllowlistedResolver(Type? resolverType, int chainLength, int modifierCount)
    {
        if (resolverType is null || chainLength != 1 || modifierCount != 0)
        {
            return false;
        }

        if (resolverType == typeof(DefaultJsonTypeInfoResolver))
        {
            return true;
        }

        return typeof(JsonSerializerContext).IsAssignableFrom(resolverType);
    }

    /// <summary>
    /// Whether the recorded converter is a framework converter, whether or not the allowlist names it. This
    /// distinguishes a converter the library has not allowlisted from a converter that is not framework
    /// metadata at all.
    /// </summary>
    public static bool IsFrameworkConverter(Type converterType) =>
        IsFrameworkAssembly(converterType) &&
        typeof(JsonConverter).IsAssignableFrom(converterType);
}
