using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// The kind of node the traversal recorded. The classifier resolves the rule of a recorded node through an
/// exhaustive switch over this enumeration, so a node kind added without a rule fails the Release build.
/// </summary>
internal enum RecordedNodeKind
{
    /// <summary>A framework object contract with recorded members and constructor parameters.</summary>
    Object,

    /// <summary>An array or enumerable contract with a recorded element type.</summary>
    Enumerable,

    /// <summary>A dictionary contract with recorded key and value types.</summary>
    Dictionary,

    /// <summary>A framework scalar type, which carries no nested metadata.</summary>
    Scalar,

    /// <summary>A repeated visit of a type that was already recorded, so recursive graphs terminate.</summary>
    Reference,

    /// <summary>A node whose metadata could not be recorded, so it cannot be classified.</summary>
    Unavailable,
}

/// <summary>Why a node could not be recorded.</summary>
internal enum RecordedNodeUnavailableReason
{
    /// <summary>The framework could not produce metadata for the type with these options.</summary>
    MetadataUnavailable,

    /// <summary>The node nests deeper than the traversal budget.</summary>
    TraversalBudget,
}

/// <summary>The wire-preservation behavior established for an enumerable materializer.</summary>
internal enum RecordedCollectionSemantics
{
    /// <summary>The materializer preserves every array item in its original order, including duplicates.</summary>
    OrderedWithMultiplicity,

    /// <summary>No committed wire witness establishes preservation of order and multiplicity.</summary>
    Unclassified,
}

/// <summary>The construction behavior established for a dictionary materializer.</summary>
internal enum RecordedDictionaryMaterialization
{
    /// <summary>The framework reader constructs and populates this concrete dictionary shape.</summary>
    Constructible,

    /// <summary>No committed wire witness establishes that the framework reader can construct the type.</summary>
    Unclassified,
}

/// <summary>One converter the traversal recorded, with the source and the path that declared or registered it.</summary>
internal sealed record RecordedConverterFact(
    MetadataSourceKind Source,
    string Path,
    Type TargetType,
    Type ConverterType);

/// <summary>The metadata resolver that produced the recorded contract.</summary>
internal sealed record RecordedResolverFact(
    string Path,
    Type? ResolverType,
    int ChainLength,
    int ModifierCount);

/// <summary>A structural child of a recorded node, reached through one discovery source.</summary>
internal sealed record RecordedEdge(MetadataSourceKind Source, RecordedNode Node);

/// <summary>A type registered in <c>JsonPolymorphismOptions.DerivedTypes</c>.</summary>
internal sealed record RecordedDerivedType(string Discriminator, string TypeName, RecordedNode Node);

/// <summary>One enum member and the JSON token the effective framework converter writes for it.</summary>
internal sealed record RecordedEnumMember(string Name, string Token, bool IsString);

/// <summary>
/// The recorded wire identity of an enum contract: the serialized name of every member when the effective
/// framework converter writes strings, or the numeric value of every member otherwise.
/// </summary>
internal sealed record RecordedEnumWire(
    bool WritesStringTokens,
    bool Unresolved,
    RecordedConverterConfiguration? ConverterConfiguration,
    IReadOnlyList<RecordedEnumMember> Members);

/// <summary>The constructor parameter associated with an effective JSON member, when one exists.</summary>
internal sealed record RecordedConstructorBinding(string Name, int Position, bool HasDefaultValue);

/// <summary>
/// One recorded member of an object contract, with the member-level metadata that decides its wire shape and
/// the recorded node of its declared type.
/// </summary>
internal sealed record RecordedMember(
    string Name,
    Type DeclaredType,
    bool Required,
    bool GetNullable,
    bool SetNullable,
    bool ExtensionData,
    bool CanSerialize,
    bool CanDeserialize,
    RecordedEnumWire? EnumWire,
    IReadOnlyList<RecordedConverterFact> ConverterFacts,
    IReadOnlyList<RecordedAttributeFact> DeclaredAttributes,
    RecordedNode Shape)
{
    /// <summary>The path from the root contract to this member.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// True when the member's metadata is decided by a converter the allowlist does not recognize. Such a
    /// member records no shape: resolving the shape would describe metadata the contract cannot classify.
    /// </summary>
    public bool MetadataNotResolved { get; init; }

    /// <summary>Whether this member is effective in the JSON contract.</summary>
    public bool Included { get; init; } = true;

    /// <summary>The constructor parameter bound to this member, when the framework reports one.</summary>
    public RecordedConstructorBinding? ConstructorBinding { get; init; }
}

/// <summary>
/// One node of the recorded contract graph. Every fact the classifier reads is captured here by the single
/// traversal, so classification never resolves metadata again and cannot disagree with what was recorded.
/// </summary>
internal sealed class RecordedNode
{
    /// <summary>The discovery source that reached this node.</summary>
    public MetadataSourceKind Source { get; init; }

    /// <summary>The name of the type this node records.</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>The type this node records, when metadata for it exists.</summary>
    public Type? Type { get; init; }

    /// <summary>Whether this exact value slot accepts the JSON null token.</summary>
    public bool AcceptsNull { get; init; }

    /// <summary>The recorded node kind.</summary>
    public RecordedNodeKind Kind { get; set; }

    /// <summary>The kind the framework reported, when metadata for the type could be resolved.</summary>
    public JsonTypeInfoKind? FrameworkKind { get; init; }

    /// <summary>The path from the root contract to this node, in developer-facing wording.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The resolver that produced this contract.</summary>
    public RecordedResolverFact? Resolver { get; set; }

    /// <summary>The wire-affecting serializer option values this contract was recorded under.</summary>
    public RecordedOptionSet Options { get; set; } = RecordedOptionSet.None;

    /// <summary>The declared JSON serialization attributes read from reflection for this type.</summary>
    public List<RecordedAttributeFact> DeclaredAttributes { get; } = new();

    /// <summary>The converters declared or registered for this contract.</summary>
    public List<RecordedConverterFact> ConverterFacts { get; } = new();

    /// <summary>True when the members of an object contract were enumerated and recorded.</summary>
    public bool MembersRecorded { get; set; }

    /// <summary>True when the public constructors of an object contract were enumerated and recorded.</summary>
    public bool ConstructorsRecorded { get; set; }

    /// <summary>True when the element type of an enumerable contract was recorded.</summary>
    public bool ElementTypeRecorded { get; set; }

    /// <summary>The measured ordering and multiplicity behavior of an enumerable materializer.</summary>
    public RecordedCollectionSemantics? CollectionSemantics { get; set; }

    /// <summary>True when the key type of a dictionary contract was recorded.</summary>
    public bool KeyTypeRecorded { get; set; }

    /// <summary>True when the value type of a dictionary contract was recorded.</summary>
    public bool ValueTypeRecorded { get; set; }

    /// <summary>The measured construction behavior of a dictionary materializer.</summary>
    public RecordedDictionaryMaterialization? DictionaryMaterialization { get; set; }

    /// <summary>Why this node could not be recorded, when it could not.</summary>
    public RecordedNodeUnavailableReason? UnavailableReason { get; set; }

    /// <summary>The node this node refers to, when the type was already recorded.</summary>
    public RecordedNode? Reference { get; set; }

    /// <summary>The members of an object contract, ordered by member name.</summary>
    public List<RecordedMember> Members { get; } = new();

    /// <summary>The types registered for a polymorphic contract, ordered by discriminator.</summary>
    public List<RecordedDerivedType> DerivedTypes { get; } = new();

    /// <summary>The discriminator property name of a polymorphic contract.</summary>
    public string? DiscriminatorPropertyName { get; set; }

    /// <summary>The recorded handling of unrecognized discriminators of a polymorphic contract.</summary>
    public string? UnknownDerivedTypeHandling { get; set; }

    /// <summary>
    /// Whether the declared object type is abstract or an interface, so the framework reader requires
    /// registered polymorphic metadata and a discriminator before it can select a materializable derived type.
    /// This fact is recorded independently of whether polymorphic metadata exists.
    /// </summary>
    public bool ReaderMaterializationRequiresDiscriminator { get; set; }

    /// <summary>The structural children reached through element, key, value, constructor, or capture sources.</summary>
    public List<RecordedEdge> Edges { get; } = new();

    /// <summary>The recorded wire identity when this node is an enum contract.</summary>
    public RecordedEnumWire? EnumWire { get; set; }
}

/// <summary>
/// The recorded classification of one node or member: whether the recorded metadata is classifiable, the
/// documented rule that decided it, and the developer-facing reason when it is not.
/// </summary>
internal sealed record Classification(bool Supported, string RuleId, string? Reason)
{
    public static Classification Classifiable(string ruleId) => new(true, ruleId, null);

    public static Classification Unclassifiable(string ruleId, string reason) => new(false, ruleId, reason);
}
