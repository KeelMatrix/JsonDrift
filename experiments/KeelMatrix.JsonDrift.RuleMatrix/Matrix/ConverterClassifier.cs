using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Classifies converters by declared type identity. A converter the library does not know cannot be
/// classified from metadata, so it is reported as unsupported instead of being assumed compatible.
/// All nested metadata discovery happens in one bounded recursive walk, defined by
/// <see cref="MetadataDiscoverySources"/>: the converter declared on the root type, on a member, on a
/// member's type, on any reachable element, key, or value type, on a registered derived type, or in the
/// serializer options, plus the identity of the metadata resolver that produced the contract.
/// </summary>
internal static class ConverterClassifier
{
    /// <summary>
    /// Traversal budget for reachable element, key, value, derived-type, and nested member types. A
    /// contract that nests deeper than this is reported unsupported rather than assumed classifiable.
    /// </summary>
    public const int MaxTraversalDepth = 8;

    /// <summary>
    /// Returns a reason when the contract cannot be classified from trustworthy metadata, otherwise null.
    /// This is the only entry point for classifier metadata: root and member classification both delegate
    /// to the same bounded walk.
    /// </summary>
    public static string? DescribeUnsupported(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        string? resolverReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.ResolverChain,
            () => DescribeUnrecognizedResolver(typeInfo));

        if (resolverReason is not null)
        {
            return resolverReason;
        }

        string? rootAttributeReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.TypeConverterAttribute,
            () => DescribeDeclared(typeInfo.Type.GetCustomAttribute<JsonConverterAttribute>(inherit: true)));

        if (rootAttributeReason is not null)
        {
            return $"root type {rootAttributeReason}";
        }

        string? rootOptionsReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.OptionsConverters,
            () => DescribeOptionsConverter(typeInfo.Options, typeInfo.Type, "root type"));

        if (rootOptionsReason is not null)
        {
            return rootOptionsReason;
        }

        return DescribeBoundedMetadata(
            MetadataDiscoverySources.TypeConverterAttribute,
            typeInfo.Options,
            typeInfo.Type,
            0,
            new HashSet<Type>(),
            "root type",
            new HashSet<string>(StringComparer.Ordinal));
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
        string? memberAttributeReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.MemberConverterAttribute,
            () => member is null
                ? null
                : DescribeDeclared(member.GetCustomAttribute<JsonConverterAttribute>(inherit: true)));

        if (memberAttributeReason is not null)
        {
            return $"member '{property.Name}' {memberAttributeReason}";
        }

        string? memberConverterReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.MemberCustomConverter,
            () => DescribeResolvedConverter(property.CustomConverter, $"member '{property.Name}'"));

        if (memberConverterReason is not null)
        {
            return memberConverterReason;
        }

        Type memberType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        string? typeAttributeReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.TypeConverterAttribute,
            () => DescribeDeclared(memberType.GetCustomAttribute<JsonConverterAttribute>(inherit: true)));

        if (typeAttributeReason is not null)
        {
            return $"member '{property.Name}' {typeAttributeReason}";
        }

        string? optionsReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.OptionsConverters,
            () => DescribeOptionsConverter(owner.Options, property.PropertyType, $"member '{property.Name}'"));

        if (optionsReason is not null)
        {
            return optionsReason;
        }

        return DescribeBoundedMetadata(
            MetadataDiscoverySources.ObjectMembers,
            owner.Options,
            memberType,
            depth + 1,
            visited,
            $"member '{property.Name}'",
            new HashSet<string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// The single recursive metadata walk. Every discovery source is routed through it, so a metadata path
    /// added without being named in <see cref="MetadataDiscoverySources"/> cannot silently stay supported.
    /// A shared visited set keeps recursive contracts finite, the visit budget stops contracts that nest
    /// deeper than the supported depth, and no application converter is ever executed.
    /// </summary>
    private static string? DescribeBoundedMetadata(
        string source,
        JsonSerializerOptions options,
        Type type,
        int depth,
        HashSet<Type> visited,
        string path,
        HashSet<string> budgetGuard)
    {
        Type shapeType = Nullable.GetUnderlyingType(type) ?? type;

        if (!budgetGuard.Add($"{depth}:{TypeShapes.TypeName(shapeType)}"))
        {
            return null;
        }

        if (depth > MaxTraversalDepth)
        {
            return MetadataDiscoverySources.Witness(
                source,
                $"{path} nests deeper than the classification depth limit ({MaxTraversalDepth})");
        }

        if (!visited.Add(shapeType))
        {
            return null;
        }

        string? declaredReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.TypeConverterAttribute,
            () => DescribeDeclared(shapeType.GetCustomAttribute<JsonConverterAttribute>(inherit: true)));

        if (declaredReason is not null)
        {
            return MetadataDiscoverySources.Witness(source, $"{path} {declaredReason}");
        }

        string? optionsReason = MetadataDiscoverySources.FindUnsupported(
            MetadataDiscoverySources.OptionsConverters,
            () => DescribeOptionsConverter(options, shapeType, path));

        if (optionsReason is not null)
        {
            return MetadataDiscoverySources.Witness(source, optionsReason);
        }

        string? polymorphismReason = DescribePolymorphismUnsupported(options, shapeType, depth, visited, path);

        if (polymorphismReason is not null)
        {
            return polymorphismReason;
        }

        if (TypeShapes.TryGetDictionaryTypes(shapeType, out Type? keyType, out Type? valueType))
        {
            string? keyReason = DescribeBoundedMetadata(
                MetadataDiscoverySources.DictionaryKeyTypes,
                options,
                keyType!,
                depth + 1,
                visited,
                $"{path} key type",
                budgetGuard);

            if (keyReason is not null)
            {
                return keyReason;
            }

            return DescribeBoundedMetadata(
                MetadataDiscoverySources.DictionaryValueTypes,
                options,
                valueType!,
                depth + 1,
                visited,
                $"{path} value type",
                budgetGuard);
        }

        if (TypeShapes.ElementType(shapeType) is Type elementType)
        {
            return DescribeBoundedMetadata(
                MetadataDiscoverySources.EnumerableElementTypes,
                options,
                elementType,
                depth + 1,
                visited,
                $"{path} element type",
                budgetGuard);
        }

        if (TypeShapes.IsScalar(shapeType))
        {
            return null;
        }

        return DescribeMembersUnsupported(
            MetadataDiscoverySources.ObjectMembers,
            options,
            shapeType,
            depth,
            visited,
            path,
            budgetGuard,
            string.Empty);
    }

    private static string? DescribePolymorphismUnsupported(
        JsonSerializerOptions options,
        Type shapeType,
        int depth,
        HashSet<Type> visited,
        string path)
    {
        JsonPolymorphismOptions? polymorphism;

        try
        {
            JsonTypeInfo polymorphic = options.GetTypeInfo(shapeType);

            if (polymorphic.Kind != JsonTypeInfoKind.Object)
            {
                return null;
            }

            string? parameterReason = DescribeConstructorParametersUnsupported(
                MetadataDiscoverySources.ConstructorParameters,
                options,
                shapeType,
                depth,
                visited,
                path);

            if (parameterReason is not null)
            {
                return parameterReason;
            }

            polymorphism = polymorphic.PolymorphismOptions;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
        {
            return null;
        }

        return DescribeRegisteredDerivedTypesUnsupported(options, polymorphism, depth, visited, path);
    }

    private static string? DescribeRegisteredDerivedTypesUnsupported(
        JsonSerializerOptions options,
        JsonPolymorphismOptions? polymorphism,
        int depth,
        HashSet<Type> visited,
        string path)
    {
        if (polymorphism is null || polymorphism.DerivedTypes.Count == 0)
        {
            return null;
        }

        foreach (JsonDerivedType derived in polymorphism.DerivedTypes
            .OrderBy(static derived => derived.TypeDiscriminator?.ToString(), StringComparer.Ordinal))
        {
            string derivedPath = $"{path} registered derived type '{derived.TypeDiscriminator}'";
            Type derivedType = derived.DerivedType;

            if (depth + 1 > MaxTraversalDepth)
            {
                return MetadataDiscoverySources.Witness(
                    MetadataDiscoverySources.PolymorphismDerivedTypes,
                    $"{derivedPath} nests deeper than the classification depth limit ({MaxTraversalDepth})");
            }

            if (!visited.Add(derivedType))
            {
                continue;
            }

            string? declaredReason = MetadataDiscoverySources.FindUnsupported(
                MetadataDiscoverySources.TypeConverterAttribute,
                () => DescribeDeclared(derivedType.GetCustomAttribute<JsonConverterAttribute>(inherit: true)));

            if (declaredReason is not null)
            {
                return MetadataDiscoverySources.Witness(
                    MetadataDiscoverySources.PolymorphismDerivedTypes,
                    $"{derivedPath} {declaredReason}");
            }

            string? optionsReason = MetadataDiscoverySources.FindUnsupported(
                MetadataDiscoverySources.OptionsConverters,
                () => DescribeOptionsConverter(options, derivedType, derivedPath));

            if (optionsReason is not null)
            {
                return optionsReason;
            }

            string? membersReason = DescribeMembersUnsupported(
                MetadataDiscoverySources.PolymorphismDerivedTypes,
                options,
                derivedType,
                depth,
                visited,
                derivedPath,
                new HashSet<string>(StringComparer.Ordinal),
                string.Empty);

            if (membersReason is not null)
            {
                return membersReason;
            }
        }

        return null;
    }

    private static string? DescribeMembersUnsupported(
        string source,
        JsonSerializerOptions options,
        Type type,
        int depth,
        HashSet<Type> visited,
        string path,
        HashSet<string> budgetGuard,
        string memberPrefix)
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
            return MetadataDiscoverySources.Witness(
                source,
                $"{path} has no metadata for these options ({exception.GetType().Name})");
        }

        if (referenced.Kind != JsonTypeInfoKind.Object)
        {
            return null;
        }

        string? parameterReason = DescribeConstructorParametersUnsupported(
            MetadataDiscoverySources.ConstructorParameters,
            options,
            shapeType,
            depth,
            visited,
            path);

        if (parameterReason is not null)
        {
            return parameterReason;
        }

        foreach (JsonPropertyInfo property in referenced.Properties)
        {
            string? extensionDataReason = MetadataDiscoverySources.FindUnsupported(
                MetadataDiscoverySources.ExtensionData,
                () => property.IsExtensionData
                    ? DescribeOptionsConverter(options, property.PropertyType, $"{path}{memberPrefix} extension-data member '{property.Name}'")
                    : null);

            if (extensionDataReason is not null)
            {
                return extensionDataReason;
            }

            string? reason = DescribeMemberUnsupported(referenced, property, depth, visited);

            if (reason is not null)
            {
                return MetadataDiscoverySources.Witness(source, $"{path}{memberPrefix} {reason}");
            }
        }

        return null;
    }

    private static string? DescribeConstructorParametersUnsupported(
        string source,
        JsonSerializerOptions options,
        Type type,
        int depth,
        HashSet<Type> visited,
        string path)
    {
        ConstructorInfo[] constructors;

        try
        {
            constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return MetadataDiscoverySources.Witness(
                source,
                $"{path} constructor metadata is unavailable ({exception.GetType().Name})");
        }

        foreach (ConstructorInfo constructor in constructors.OrderBy(
            static constructor => constructor.ToString(),
            StringComparer.Ordinal))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                string parameterPath = $"{path} constructor parameter '{parameter.Name}'";

                if (TypeShapes.IsScalar(parameter.ParameterType))
                {
                    continue;
                }

                string? reason = MetadataDiscoverySources.FindUnsupported(
                    source,
                    () => DescribeOptionsConverter(options, parameter.ParameterType, parameterPath));

                if (reason is not null)
                {
                    return reason;
                }

                string? nestedReason = DescribeBoundedMetadata(
                    source,
                    options,
                    parameter.ParameterType,
                    depth + 1,
                    visited,
                    parameterPath,
                    new HashSet<string>(StringComparer.Ordinal));

                if (nestedReason is not null)
                {
                    return nestedReason;
                }
            }
        }

        return null;
    }

    private static string? DescribeOptionsConverter(JsonSerializerOptions options, Type type, string path)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(type);

        foreach (JsonConverter converter in options.Converters)
        {
            if (converter.CanConvert(type) && !MetadataDiscoverySources.IsKnownConverterType(converter.GetType()))
            {
                return $"{path} is converted by options converter {converter.GetType().FullName}";
            }
        }

        return null;
    }

    private static string? DescribeResolvedConverter(JsonConverter? converter, string path)
    {
        if (converter is null)
        {
            return null;
        }

        Type converterType = converter.GetType();

        return MetadataDiscoverySources.IsKnownConverterType(converterType)
            ? null
            : $"{path} is converted by unrecognized converter {converterType.FullName}";
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
                MetadataDiscoverySources.IsKnownConverterType(converter.GetType()) &&
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
        MetadataDiscoverySources.IsKnownConverterType(converterType);

    private static bool IsFrameworkStringEnumConverter(JsonConverter? converter) =>
        converter is JsonStringEnumConverter && MetadataDiscoverySources.IsKnownConverterType(converter.GetType());

    private static string? DescribeDeclared(JsonConverterAttribute? attribute)
    {
        if (attribute?.ConverterType is null)
        {
            return null;
        }

        return MetadataDiscoverySources.IsKnownConverterType(attribute.ConverterType)
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
