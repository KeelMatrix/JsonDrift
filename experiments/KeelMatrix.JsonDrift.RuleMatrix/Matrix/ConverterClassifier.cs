using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Classifies converters by declared type identity. A converter the library does not know cannot be
/// classified from metadata, so it is reported as unsupported instead of being assumed compatible.
/// The declared metadata considered here is the converter attribute on the member, on the member type, on
/// an element, key, or value type reachable from the member, or on the root type, plus converters
/// registered on the serializer options.
/// </summary>
internal static class ConverterClassifier
{
    /// <summary>
    /// Traversal budget for reachable element, key, value, and nested member types. A contract that nests
    /// deeper than this is reported unsupported rather than assumed classifiable.
    /// </summary>
    public const int MaxTraversalDepth = 8;

    /// <summary>
    /// A converter is known only when it ships in the framework <c>System.Text.Json</c> assembly. A
    /// namespace prefix is not evidence of framework provenance, because application converters may
    /// declare a <c>System.Text.Json</c> namespace of their own.
    /// </summary>
    public static bool IsKnownConverterType(Type converterType)
    {
        ArgumentNullException.ThrowIfNull(converterType);

        return converterType.Assembly == typeof(JsonSerializer).Assembly;
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

        string? rootOptionsReason = DescribeOptionsConverter(typeInfo.Options, typeInfo.Type, "root type");

        if (rootOptionsReason is not null)
        {
            return rootOptionsReason;
        }

        return DescribeTypeUnsupported(typeInfo.Options, typeInfo.Type, 0, new HashSet<Type>(), "root type");
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

    public static string? DescribeMemberUnsupported(JsonTypeInfo owner, JsonPropertyInfo property) =>
        DescribeMemberUnsupported(owner, property, 0, new HashSet<Type>());

    private static string? DescribeMemberUnsupported(
        JsonTypeInfo owner,
        JsonPropertyInfo property,
        int depth,
        HashSet<Type> visited)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(property);

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

        string? optionsReason = DescribeOptionsConverter(owner.Options, property.PropertyType, $"member '{property.Name}'");

        if (optionsReason is not null)
        {
            return optionsReason;
        }

        return DescribeTypeUnsupported(owner.Options, memberType, depth + 1, visited, $"member '{property.Name}'");
    }

    /// <summary>
    /// Walks element, key, and value types of a declared member type, then the members of a referenced
    /// object contract, so an unrecognized converter can never hide below the first level. The same
    /// converter check applies at every level, and exhausting the traversal budget is reported as
    /// unsupported rather than as classifiable.
    /// </summary>
    private static string? DescribeTypeUnsupported(
        JsonSerializerOptions options,
        Type type,
        int depth,
        HashSet<Type> visited,
        string path)
    {
        Type shapeType = Nullable.GetUnderlyingType(type) ?? type;

        if (depth > MaxTraversalDepth)
        {
            return $"{path} nests deeper than the classification depth limit ({MaxTraversalDepth})";
        }

        if (!visited.Add(shapeType))
        {
            return null;
        }

        string? declaredReason = DescribeDeclared(shapeType.GetCustomAttribute<JsonConverterAttribute>(inherit: true));

        if (declaredReason is not null)
        {
            return $"{path} {declaredReason}";
        }

        string? optionsReason = DescribeOptionsConverter(options, shapeType, path);

        if (optionsReason is not null)
        {
            return optionsReason;
        }

        if (TypeShapes.TryGetDictionaryTypes(shapeType, out Type? keyType, out Type? valueType))
        {
            string? keyReason = DescribeTypeUnsupported(options, keyType!, depth + 1, visited, $"{path} key type");

            if (keyReason is not null)
            {
                return keyReason;
            }

            return DescribeTypeUnsupported(options, valueType!, depth + 1, visited, $"{path} value type");
        }

        if (TypeShapes.ElementType(shapeType) is Type elementType)
        {
            return DescribeTypeUnsupported(options, elementType, depth + 1, visited, $"{path} element type");
        }

        if (TypeShapes.IsScalar(shapeType))
        {
            return null;
        }

        return DescribeNestedMembersUnsupported(options, shapeType, depth, visited, path);
    }

    private static string? DescribeNestedMembersUnsupported(
        JsonSerializerOptions options,
        Type type,
        int depth,
        HashSet<Type> visited,
        string path)
    {
        Type shapeType = Nullable.GetUnderlyingType(type) ?? type;

        if (TypeShapes.IsScalar(shapeType) ||
            TypeShapes.ElementType(shapeType) is not null ||
            TypeShapes.TryGetDictionaryTypes(shapeType, out _, out _))
        {
            return null;
        }

        JsonTypeInfo referenced;

        try
        {
            referenced = options.GetTypeInfo(shapeType);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
        {
            return $"{path} has no metadata for these options ({exception.GetType().Name})";
        }

        if (referenced.Kind != JsonTypeInfoKind.Object)
        {
            return null;
        }

        foreach (JsonPropertyInfo property in referenced.Properties)
        {
            string? reason = DescribeMemberUnsupported(referenced, property, depth, visited);

            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    private static string? DescribeOptionsConverter(JsonSerializerOptions options, Type type, string path)
    {
        foreach (JsonConverter converter in options.Converters)
        {
            if (converter.CanConvert(type) && !IsKnownConverterType(converter.GetType()))
            {
                return $"{path} is converted by options converter {converter.GetType().FullName}";
            }
        }

        return null;
    }

    /// <summary>
    /// True when the declared metadata shows that an enum member is written as a JSON string.
    /// </summary>
    public static bool WritesStringTokens(JsonTypeInfo owner, JsonPropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(property);

        MemberInfo? member = ResolveMember(owner.Type, property);

        if (IsStringEnumConverter(member?.GetCustomAttribute<JsonConverterAttribute>(inherit: true)) ||
            IsFrameworkStringEnumConverter(property.CustomConverter))
        {
            return true;
        }

        return WritesStringTokens(owner.Options, property.PropertyType);
    }

    /// <summary>
    /// True when the declared metadata of a type shows that its enum members are written as JSON strings.
    /// </summary>
    public static bool WritesStringTokens(JsonSerializerOptions options, Type type)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(type);

        Type shapeType = Nullable.GetUnderlyingType(type) ?? type;

        if (IsStringEnumConverter(shapeType.GetCustomAttribute<JsonConverterAttribute>(inherit: true)))
        {
            return true;
        }

        return FindOptionsStringEnumConverter(options, type) is not null;
    }

    private static JsonStringEnumConverter? FindOptionsStringEnumConverter(JsonSerializerOptions options, Type type)
    {
        foreach (JsonConverter converter in options.Converters)
        {
            if (converter is JsonStringEnumConverter stringEnumConverter &&
                IsKnownConverterType(converter.GetType()) &&
                converter.CanConvert(type))
            {
                return stringEnumConverter;
            }
        }

        return null;
    }

    private static bool IsStringEnumConverter(JsonConverterAttribute? attribute) =>
        attribute?.ConverterType is Type converterType &&
        typeof(JsonStringEnumConverter).IsAssignableFrom(converterType) &&
        IsKnownConverterType(converterType);

    private static bool IsFrameworkStringEnumConverter(JsonConverter? converter) =>
        converter is JsonStringEnumConverter && IsKnownConverterType(converter.GetType());

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

    public static MemberInfo? ResolveMember(Type type, JsonPropertyInfo property)
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
