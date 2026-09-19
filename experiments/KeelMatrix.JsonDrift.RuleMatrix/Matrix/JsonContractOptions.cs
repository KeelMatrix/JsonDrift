using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Creates the serializer option sets used by the rule matrix.
/// </summary>
internal static class JsonContractOptions
{
    /// <summary>
    /// The option profiles every committed check is measured under, and the single source of truth of the
    /// classifier's option allowlist.
    /// </summary>
    private static readonly Func<JsonSerializerOptions>[] Profiles =
    {
        static () => Reflection(),
        static () => OmitsNullMembers(),
    };

    /// <summary>
    /// Reflection-based options with an explicit resolver. <see cref="JsonSerializerOptions.GetTypeInfo"/>
    /// requires a resolver; default options are usable through <see cref="JsonSerializer"/> only.
    /// </summary>
    public static JsonSerializerOptions Reflection(params JsonConverter[] converters)
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        foreach (JsonConverter converter in converters)
        {
            options.Converters.Add(converter);
        }

        return options;
    }

    /// <summary>
    /// Reflection-based options that leave a member out of the document when its value is null, which is the
    /// option set the checks that measure null-writing and source-generated contexts are measured under.
    /// </summary>
    public static JsonSerializerOptions OmitsNullMembers(params JsonConverter[] converters)
    {
        JsonSerializerOptions options = Reflection(converters);
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }

    /// <summary>
    /// The option values the committed checks are measured under, read back from the profiles above rather
    /// than written out a second time, so the classifier's option allowlist cannot drift from the option sets
    /// the matrix runs under. A value that no profile produces is not accepted: a contract recorded under it
    /// is reported through <c>unsupported.option-unlisted</c>.
    /// </summary>
    public static IReadOnlyList<RecordedOptionValue> MeasuredOptionValues { get; } = Profiles
        .SelectMany(static profile => SerializerOptionFacts.Read(profile()).Values)
        .Distinct()
        .OrderBy(static value => value.Kind)
        .ThenBy(static value => value.Value, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// The allowlisted enum converter configurations measured by executing their integer-token reads. The
    /// profiles are the source of truth for ContractAllowlists and for the converter-configuration checks.
    /// </summary>
    public static IReadOnlyList<RecordedConverterConfiguration> MeasuredConverterConfigurations { get; } =
        new (JsonSerializerOptions Options, Type EnumType)[]
        {
            (Reflection(), typeof(OrderState)),
            (Reflection(new JsonStringEnumConverter()), typeof(OrderState)),
            (Reflection(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)), typeof(OrderState)),
        }
        .Select(profile => ConverterConfigurationFacts.Probe(profile.Options, profile.EnumType)!)
        .Distinct()
        .OrderBy(static value => value.ConverterType, StringComparer.Ordinal)
        .ThenBy(static value => value.IntegerTokensAccepted)
        .ToArray();
}
