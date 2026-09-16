using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Classifies converters by declared type identity. A converter the library does not know cannot be
/// classified from metadata, so it is reported as unsupported instead of being assumed compatible.
/// The declared metadata considered here is the converter attribute on the member, on the member type,
/// or on the root type, plus converters registered on the serializer options.
/// </summary>
internal static class ConverterClassifier
{
    private const string FrameworkNamespace = "System.Text.Json";

    public static bool IsKnownConverterType(Type converterType)
    {
        ArgumentNullException.ThrowIfNull(converterType);

        string? ns = converterType.Namespace;
        return ns is not null && ns.StartsWith(FrameworkNamespace, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns a reason when the contract cannot be classified from trustworthy metadata, otherwise null.
    /// </summary>
    public static string? DescribeUnsupported(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        string? resolverReason = DescribeUnrecognizedResolver(typeInfo);

        if (resolverReason is not null)
        {
            return resolverReason;
        }

        string? rootReason = DescribeDeclared(typeInfo.Type.GetCustomAttribute<JsonConverterAttribute>(inherit: true));

        if (rootReason is not null)
        {
            return $"root type {rootReason}";
        }

        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            string? reason = DescribeMemberUnsupported(typeInfo, property);

            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    public static bool IsSupported(JsonTypeInfo typeInfo) => DescribeUnsupported(typeInfo) is null;

    /// <summary>
    /// Metadata is only trustworthy when it comes from a known metadata source. A custom resolver can
    /// replace member converters without leaving any declared marker, so it is reported as unsupported.
    /// </summary>
    public static string? DescribeUnrecognizedResolver(JsonTypeInfo typeInfo)
    {
        IJsonTypeInfoResolver? resolver = typeInfo.Options.TypeInfoResolver;

        if (resolver is null)
        {
            return "no metadata resolver is configured for these options";
        }

        if (resolver is JsonSerializerContext || resolver.GetType() == typeof(DefaultJsonTypeInfoResolver))
        {
            return null;
        }

        return $"metadata was produced by unrecognized resolver {resolver.GetType().FullName}";
    }

    public static string? DescribeMemberUnsupported(JsonTypeInfo owner, JsonPropertyInfo property)
    {
        MemberInfo? member = ResolveMember(owner.Type, property);
        string? memberReason = member is null
            ? null
            : DescribeDeclared(member.GetCustomAttribute<JsonConverterAttribute>(inherit: true));

        if (memberReason is not null)
        {
            return $"member '{property.Name}' {memberReason}";
        }

        Type memberType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        string? typeReason = DescribeDeclared(memberType.GetCustomAttribute<JsonConverterAttribute>(inherit: true));

        if (typeReason is not null)
        {
            return $"member '{property.Name}' {typeReason}";
        }

        foreach (JsonConverter converter in owner.Options.Converters)
        {
            if (converter.CanConvert(property.PropertyType) && !IsKnownConverterType(converter.GetType()))
            {
                return $"member '{property.Name}' is converted by options converter {converter.GetType().FullName}";
            }
        }

        return null;
    }

    /// <summary>
    /// True when the declared metadata shows that an enum member is written as a JSON string.
    /// </summary>
    public static bool WritesStringTokens(JsonTypeInfo owner, JsonPropertyInfo property)
    {
        Type memberType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        MemberInfo? member = ResolveMember(owner.Type, property);

        foreach (JsonConverterAttribute? attribute in new[]
        {
            member?.GetCustomAttribute<JsonConverterAttribute>(inherit: true),
            memberType.GetCustomAttribute<JsonConverterAttribute>(inherit: true),
        })
        {
            if (attribute?.ConverterType == typeof(JsonStringEnumConverter))
            {
                return true;
            }
        }

        foreach (JsonConverter converter in owner.Options.Converters)
        {
            if (converter is JsonStringEnumConverter && converter.CanConvert(property.PropertyType))
            {
                return true;
            }
        }

        return false;
    }

    private static string? DescribeDeclared(JsonConverterAttribute? attribute)
    {
        if (attribute?.ConverterType is null)
        {
            return null;
        }

        return IsKnownConverterType(attribute.ConverterType)
            ? null
            : $"declares unrecognized converter {attribute.ConverterType.FullName}";
    }

    private static MemberInfo? ResolveMember(Type type, JsonPropertyInfo property)
    {
        const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        MethodInfo? accessor = property.Get?.Method ?? property.Set?.Method;

        if (accessor is not null && accessor.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            string name = accessor.Name[4..];

            MemberInfo? resolved =
                (MemberInfo?)type.GetProperty(name, InstanceMembers) ??
                type.GetField(name, InstanceMembers);

            if (resolved is not null)
            {
                return resolved;
            }
        }

        return
            (MemberInfo?)type.GetProperty(property.Name, InstanceMembers) ??
            type.GetField(property.Name, InstanceMembers);
    }
}
