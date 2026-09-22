namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// Every metadata discovery source the single recursive traversal records. The traversal resolves the
/// discovery source of every nested type, and the documentation and coverage rules resolve the identifier,
/// the description, and the classification rules of every source, through exhaustive switches over this
/// enumeration. A source added here without being handled therefore fails the Release build instead of
/// passing by default.
/// </summary>
internal enum MetadataSourceKind
{
    /// <summary>The identity of the metadata resolver that produced the contract.</summary>
    ResolverChain,

    /// <summary>A converter attribute declared on a visited type.</summary>
    TypeConverterAttribute,

    /// <summary>A converter attribute declared on the member that reaches a visited type.</summary>
    MemberConverterAttribute,

    /// <summary>A converter the metadata provider assigned to a member without a converter attribute.</summary>
    MemberCustomConverter,

    /// <summary>A converter registered in <c>JsonSerializerOptions.Converters</c>.</summary>
    OptionsConverters,

    /// <summary>The members of a visited object contract.</summary>
    ObjectMembers,

    /// <summary>The captured value type of a <c>JsonExtensionData</c> member.</summary>
    ExtensionData,

    /// <summary>The parameter types of the constructors of a visited object contract.</summary>
    ConstructorParameters,

    /// <summary>The element type of a visited array or enumerable contract.</summary>
    EnumerableElementTypes,

    /// <summary>The key type of a visited dictionary contract.</summary>
    DictionaryKeyTypes,

    /// <summary>The value type of a visited dictionary contract.</summary>
    DictionaryValueTypes,

    /// <summary>The types registered through <c>JsonPolymorphismOptions.DerivedTypes</c>.</summary>
    PolymorphismDerivedTypes,

    /// <summary>The wire-affecting setting values of the <c>JsonSerializerOptions</c> the contract is recorded under.</summary>
    OptionsSettings,

    /// <summary>System.Text.Json serialization attributes declared on visited contract types, constructors, members, and enum fields.</summary>
    DeclaredAttributes,

    /// <summary>Observable configuration probed by executing an allowlisted framework enum converter.</summary>
    ConverterConfiguration,
}

/// <summary>
/// The developer-facing identity of the discovery sources. Every switch over
/// <see cref="MetadataSourceKind"/> here is exhaustive and compiled with warnings as errors, so a source
/// that is added without an identifier, a description, and a set of classification rules fails the build.
/// </summary>
internal static class MetadataSourceRules
{
    /// <summary>Every declared discovery source, in declaration order.</summary>
    public static readonly MetadataSourceKind[] All = Enum.GetValues<MetadataSourceKind>();

    /// <summary>The stable identifier used by the path inventory and by the executed checks.</summary>
    public static string Id(MetadataSourceKind source) => source switch
    {
        MetadataSourceKind.ResolverChain => "resolver-chain",
        MetadataSourceKind.TypeConverterAttribute => "type-converter-attribute",
        MetadataSourceKind.MemberConverterAttribute => "member-converter-attribute",
        MetadataSourceKind.MemberCustomConverter => "member-custom-converter",
        MetadataSourceKind.OptionsConverters => "options-converters",
        MetadataSourceKind.ObjectMembers => "object-members",
        MetadataSourceKind.ExtensionData => "extension-data",
        MetadataSourceKind.ConstructorParameters => "constructor-parameters",
        MetadataSourceKind.EnumerableElementTypes => "enumerable-element-types",
        MetadataSourceKind.DictionaryKeyTypes => "dictionary-key-types",
        MetadataSourceKind.DictionaryValueTypes => "dictionary-value-types",
        MetadataSourceKind.PolymorphismDerivedTypes => "polymorphism-derived-types",
        MetadataSourceKind.OptionsSettings => "options-settings",
        MetadataSourceKind.DeclaredAttributes => "declared-attributes",
        MetadataSourceKind.ConverterConfiguration => "converter-configuration",
    };

    /// <summary>What the source discovers, in the developer-facing wording of the path inventory.</summary>
    public static string Description(MetadataSourceKind source) => source switch
    {
        MetadataSourceKind.ResolverChain => "the identity of the metadata resolver that produced the contract",
        MetadataSourceKind.TypeConverterAttribute => "a converter attribute declared on a visited type",
        MetadataSourceKind.MemberConverterAttribute => "a converter attribute declared on the member that reaches a type",
        MetadataSourceKind.MemberCustomConverter => "a converter assigned to a member by the metadata provider",
        MetadataSourceKind.OptionsConverters => "a converter registered in JsonSerializerOptions.Converters",
        MetadataSourceKind.ObjectMembers => "the members of a visited object contract",
        MetadataSourceKind.ExtensionData => "the captured value type of a JsonExtensionData member",
        MetadataSourceKind.ConstructorParameters => "the parameter types of the constructors of a visited contract",
        MetadataSourceKind.EnumerableElementTypes => "the element type of a visited array or enumerable contract",
        MetadataSourceKind.DictionaryKeyTypes => "the key type of a visited dictionary contract",
        MetadataSourceKind.DictionaryValueTypes => "the value type of a visited dictionary contract",
        MetadataSourceKind.PolymorphismDerivedTypes => "the types registered in JsonPolymorphismOptions.DerivedTypes",
        MetadataSourceKind.OptionsSettings => "the wire-affecting setting values of JsonSerializerOptions",
        MetadataSourceKind.DeclaredAttributes => "the declared System.Text.Json.Serialization attribute facts on contract types, constructors, members, and enum fields",
        MetadataSourceKind.ConverterConfiguration => "the integer-token acceptance observed by executing an allowlisted enum converter",
    };

    /// <summary>
    /// The classification rules that can be reached through the source. The coverage check requires every
    /// rule of every visited source to exist in the documented rule catalogue, so a source cannot be walked
    /// without a rule that explains what a fact discovered through it means.
    /// </summary>
    public static IReadOnlyList<string> ClassificationRules(MetadataSourceKind source) => source switch
    {
        MetadataSourceKind.ResolverChain => new[]
        {
            RuleIds.SupportedResolverDefaultReflection,
            RuleIds.SupportedResolverSourceGenerated,
            RuleIds.UnsupportedResolverUnrecognized,
            RuleIds.UnsupportedResolverChain,
            RuleIds.UnsupportedResolverModifiers,
        },
        MetadataSourceKind.TypeConverterAttribute => new[]
        {
            RuleIds.UnsupportedConverterUnrecognized,
            RuleIds.UnsupportedConverterUnlisted,
        },
        MetadataSourceKind.MemberConverterAttribute => new[]
        {
            RuleIds.UnsupportedConverterUnrecognized,
            RuleIds.UnsupportedConverterUnlisted,
        },
        MetadataSourceKind.MemberCustomConverter => new[]
        {
            RuleIds.UnsupportedConverterUnrecognized,
            RuleIds.UnsupportedConverterUnlisted,
        },
        MetadataSourceKind.OptionsConverters => new[]
        {
            RuleIds.UnsupportedConverterUnrecognized,
            RuleIds.UnsupportedConverterUnlisted,
        },
        MetadataSourceKind.ObjectMembers => new[]
        {
            RuleIds.SupportedObject,
            RuleIds.SupportedMember,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedMetadataUnavailable,
            RuleIds.UnsupportedObjectMaterializationUnproven,
        },
        MetadataSourceKind.ExtensionData => new[]
        {
            RuleIds.SupportedObject,
            RuleIds.SupportedMember,
        },
        MetadataSourceKind.ConstructorParameters => new[]
        {
            RuleIds.SupportedObject,
            RuleIds.UnsupportedShapeEvidenceMissing,
        },
        MetadataSourceKind.EnumerableElementTypes => new[]
        {
            RuleIds.SupportedEnumerable,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedTraversalBudget,
        },
        MetadataSourceKind.DictionaryKeyTypes => new[]
        {
            RuleIds.SupportedDictionary,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedTraversalBudget,
        },
        MetadataSourceKind.DictionaryValueTypes => new[]
        {
            RuleIds.SupportedDictionary,
            RuleIds.UnsupportedShapeEvidenceMissing,
            RuleIds.UnsupportedTraversalBudget,
        },
        MetadataSourceKind.PolymorphismDerivedTypes => new[]
        {
            RuleIds.SupportedObject,
            RuleIds.UnsupportedMetadataUnavailable,
        },
        MetadataSourceKind.OptionsSettings => new[]
        {
            RuleIds.UnsupportedOptionUnlisted,
        },
        MetadataSourceKind.DeclaredAttributes => new[]
        {
            RuleIds.UnsupportedAttributeUnlisted,
        },
        MetadataSourceKind.ConverterConfiguration => new[]
        {
            RuleIds.UnsupportedConverterConfigurationUnlisted,
        },
    };
}
