using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable SYSLIB0020

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// The serializer option families whose values change what a contract writes or reads. The traversal records
/// the value of every family once per contract, the classifier accepts a recorded value only when the option
/// allowlist names it, and the canonical document records the values the contract was recorded under. Every
/// switch over this enumeration is exhaustive and compiled with warnings as errors, so a family added without
/// an identifier or a recorded value fails the Release build.
/// </summary>
internal enum ContractOptionKind
{
    /// <summary>Whether numbers may be written as JSON strings or read from them.</summary>
    NumberHandling,

    /// <summary>The handler that preserves object references.</summary>
    ReferenceHandler,

    /// <summary>The condition under which a member is left out of the document.</summary>
    DefaultIgnoreCondition,

    /// <summary>What the reader does with a member that has no matching property.</summary>
    UnmappedMemberHandling,

    /// <summary>Whether member names are matched without regard to case.</summary>
    PropertyNameCaseInsensitive,

    /// <summary>What the reader does with a comment.</summary>
    ReadCommentHandling,

    /// <summary>Whether the reader accepts a trailing comma.</summary>
    AllowTrailingCommas,

    /// <summary>The nesting limit the reader and writer enforce.</summary>
    MaxDepth,

    /// <summary>The policy applied to dictionary keys.</summary>
    DictionaryKeyPolicy,

    /// <summary>Whether read-only properties are left out of the document.</summary>
    IgnoreReadOnlyProperties,

    /// <summary>Whether read-only fields are left out of the document.</summary>
    IgnoreReadOnlyFields,

    /// <summary>The policy applied to member names.</summary>
    PropertyNamingPolicy,

    /// <summary>Whether the reader enforces nullable annotations.</summary>
    RespectNullableAnnotations,

    /// <summary>Whether the reader requires a bound constructor parameter to be present.</summary>
    RespectRequiredConstructorParameters,

    /// <summary>The handling used when a member is populated by deserialization.</summary>
    PreferredObjectCreationHandling,

    /// <summary>Whether duplicate JSON property names are accepted by the reader.</summary>
    AllowDuplicateProperties,

    /// <summary>Whether polymorphic metadata may appear after ordinary properties.</summary>
    AllowOutOfOrderMetadataProperties,

    /// <summary>Whether null-valued members are omitted and ignored.</summary>
    IgnoreNullValues,

    /// <summary>Whether public fields participate in the contract.</summary>
    IncludeFields,

    /// <summary>How values typed as <c>object</c> are materialized by the reader.</summary>
    UnknownTypeHandling,
}

/// <summary>One recorded serializer option value.</summary>
internal sealed record RecordedOptionValue(ContractOptionKind Kind, string Value)
{
    /// <summary>The stable identifier used by the canonical document, the allowlist, and the checks.</summary>
    public string Id => SerializerOptionFacts.Id(Kind);

    /// <summary>The <c>id=value</c> form used by the option allowlist and by the reported reason.</summary>
    public string Display => $"{Id}={Value}";
}

/// <summary>
/// The serializer option values recorded for one contract, in family order, so a recorded contract carries
/// the whole option set a verdict may rest on instead of an unrecorded subset of it.
/// </summary>
internal sealed record RecordedOptionSet(IReadOnlyList<RecordedOptionValue> Values)
{
    /// <summary>An option set with no recorded family, which the classifier reports as missing evidence.</summary>
    public static RecordedOptionSet None { get; } = new(Array.Empty<RecordedOptionValue>());

    /// <summary>True when no option family was recorded for the contract.</summary>
    public bool IsEmpty => Values.Count == 0;
}

/// <summary>
/// Reads the recorded value of every wire-affecting serializer option family. The values are recorded as
/// plain text so the canonical document, the option allowlist, and the reported reason all name the same
/// value, and a value the allowlist does not name is visible instead of being silently unmodelled.
/// </summary>
internal static class SerializerOptionFacts
{
    /// <summary>The literal recorded in place of a handler or policy that no option set installs.</summary>
    public const string DefaultValue = "Default";

    /// <summary>Every declared option family, in the order the traversal records it.</summary>
    public static readonly ContractOptionKind[] All = Enum.GetValues<ContractOptionKind>();

    /// <summary>The stable identifier of an option family.</summary>
    public static string Id(ContractOptionKind kind) => kind switch
    {
        ContractOptionKind.NumberHandling => "numberHandling",
        ContractOptionKind.ReferenceHandler => "referenceHandler",
        ContractOptionKind.DefaultIgnoreCondition => "defaultIgnoreCondition",
        ContractOptionKind.UnmappedMemberHandling => "unmappedMemberHandling",
        ContractOptionKind.PropertyNameCaseInsensitive => "propertyNameCaseInsensitive",
        ContractOptionKind.ReadCommentHandling => "readCommentHandling",
        ContractOptionKind.AllowTrailingCommas => "allowTrailingCommas",
        ContractOptionKind.MaxDepth => "maxDepth",
        ContractOptionKind.DictionaryKeyPolicy => "dictionaryKeyPolicy",
        ContractOptionKind.IgnoreReadOnlyProperties => "ignoreReadOnlyProperties",
        ContractOptionKind.IgnoreReadOnlyFields => "ignoreReadOnlyFields",
        ContractOptionKind.PropertyNamingPolicy => "propertyNamingPolicy",
        ContractOptionKind.RespectNullableAnnotations => "respectNullableAnnotations",
        ContractOptionKind.RespectRequiredConstructorParameters => "respectRequiredConstructorParameters",
        ContractOptionKind.PreferredObjectCreationHandling => "preferredObjectCreationHandling",
        ContractOptionKind.AllowDuplicateProperties => "allowDuplicateProperties",
        ContractOptionKind.AllowOutOfOrderMetadataProperties => "allowOutOfOrderMetadataProperties",
        ContractOptionKind.IgnoreNullValues => "ignoreNullValues",
        ContractOptionKind.IncludeFields => "includeFields",
        ContractOptionKind.UnknownTypeHandling => "unknownTypeHandling",
    };

    /// <summary>The public <see cref="JsonSerializerOptions"/> property represented by an option family.</summary>
    public static string PropertyName(ContractOptionKind kind) => kind switch
    {
        ContractOptionKind.NumberHandling => nameof(JsonSerializerOptions.NumberHandling),
        ContractOptionKind.ReferenceHandler => nameof(JsonSerializerOptions.ReferenceHandler),
        ContractOptionKind.DefaultIgnoreCondition => nameof(JsonSerializerOptions.DefaultIgnoreCondition),
        ContractOptionKind.UnmappedMemberHandling => nameof(JsonSerializerOptions.UnmappedMemberHandling),
        ContractOptionKind.PropertyNameCaseInsensitive => nameof(JsonSerializerOptions.PropertyNameCaseInsensitive),
        ContractOptionKind.ReadCommentHandling => nameof(JsonSerializerOptions.ReadCommentHandling),
        ContractOptionKind.AllowTrailingCommas => nameof(JsonSerializerOptions.AllowTrailingCommas),
        ContractOptionKind.MaxDepth => nameof(JsonSerializerOptions.MaxDepth),
        ContractOptionKind.DictionaryKeyPolicy => nameof(JsonSerializerOptions.DictionaryKeyPolicy),
        ContractOptionKind.IgnoreReadOnlyProperties => nameof(JsonSerializerOptions.IgnoreReadOnlyProperties),
        ContractOptionKind.IgnoreReadOnlyFields => nameof(JsonSerializerOptions.IgnoreReadOnlyFields),
        ContractOptionKind.PropertyNamingPolicy => nameof(JsonSerializerOptions.PropertyNamingPolicy),
        ContractOptionKind.RespectNullableAnnotations => nameof(JsonSerializerOptions.RespectNullableAnnotations),
        ContractOptionKind.RespectRequiredConstructorParameters => nameof(JsonSerializerOptions.RespectRequiredConstructorParameters),
        ContractOptionKind.PreferredObjectCreationHandling => nameof(JsonSerializerOptions.PreferredObjectCreationHandling),
        ContractOptionKind.AllowDuplicateProperties => nameof(JsonSerializerOptions.AllowDuplicateProperties),
        ContractOptionKind.AllowOutOfOrderMetadataProperties => nameof(JsonSerializerOptions.AllowOutOfOrderMetadataProperties),
        ContractOptionKind.IgnoreNullValues => nameof(JsonSerializerOptions.IgnoreNullValues),
        ContractOptionKind.IncludeFields => nameof(JsonSerializerOptions.IncludeFields),
        ContractOptionKind.UnknownTypeHandling => nameof(JsonSerializerOptions.UnknownTypeHandling),
    };

    /// <summary>The recorded value of one option family.</summary>
    public static string Value(ContractOptionKind kind, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return kind switch
        {
            ContractOptionKind.NumberHandling => options.NumberHandling.ToString(),
            ContractOptionKind.ReferenceHandler => Instance(options.ReferenceHandler?.GetType()),
            ContractOptionKind.DefaultIgnoreCondition => options.DefaultIgnoreCondition.ToString(),
            ContractOptionKind.UnmappedMemberHandling => options.UnmappedMemberHandling.ToString(),
            ContractOptionKind.PropertyNameCaseInsensitive => Boolean(options.PropertyNameCaseInsensitive),
            ContractOptionKind.ReadCommentHandling => options.ReadCommentHandling.ToString(),
            ContractOptionKind.AllowTrailingCommas => Boolean(options.AllowTrailingCommas),
            ContractOptionKind.MaxDepth => options.MaxDepth.ToString(CultureInfo.InvariantCulture),
            ContractOptionKind.DictionaryKeyPolicy => Instance(options.DictionaryKeyPolicy?.GetType()),
            ContractOptionKind.IgnoreReadOnlyProperties => Boolean(options.IgnoreReadOnlyProperties),
            ContractOptionKind.IgnoreReadOnlyFields => Boolean(options.IgnoreReadOnlyFields),
            ContractOptionKind.PropertyNamingPolicy => Instance(options.PropertyNamingPolicy?.GetType()),
            ContractOptionKind.RespectNullableAnnotations => Boolean(options.RespectNullableAnnotations),
            ContractOptionKind.RespectRequiredConstructorParameters =>
                Boolean(options.RespectRequiredConstructorParameters),
            ContractOptionKind.PreferredObjectCreationHandling => options.PreferredObjectCreationHandling.ToString(),
            ContractOptionKind.AllowDuplicateProperties => Boolean(options.AllowDuplicateProperties),
            ContractOptionKind.AllowOutOfOrderMetadataProperties => Boolean(options.AllowOutOfOrderMetadataProperties),
            ContractOptionKind.IgnoreNullValues => Boolean(options.IgnoreNullValues),
            ContractOptionKind.IncludeFields => Boolean(options.IncludeFields),
            ContractOptionKind.UnknownTypeHandling => options.UnknownTypeHandling.ToString(),
        };
    }

    /// <summary>Records the value of every option family of one contract.</summary>
    public static RecordedOptionSet Read(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var values = new List<RecordedOptionValue>(All.Length);

        foreach (ContractOptionKind kind in All)
        {
            values.Add(new RecordedOptionValue(kind, Value(kind, options)));
        }

        return new RecordedOptionSet(values);
    }

    private static string Instance(Type? type) =>
        type is null ? DefaultValue : TypeShapes.TypeName(type);

    private static string Boolean(bool value) => value ? "true" : "false";
}

#pragma warning restore SYSLIB0020
