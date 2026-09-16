using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Produces a deterministic structural description of an effective serializer contract.
/// Output rules: members sorted by name, fixed key order, LF newlines, UTF-8 without a byte order mark,
/// no timestamps, no absolute paths, and nested shapes described by reference so recursive graphs terminate.
/// </summary>
internal static class ContractCanonicalizer
{
    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Canonicalize(JsonTypeInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var document = new JsonObject
        {
            ["contractVersion"] = 1,
            ["root"] = Describe(contract),
        };

        string json = document.ToJsonString(WriterOptions);
        string normalized = json.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : string.Concat(normalized, "\n");
    }

    private static JsonObject Describe(JsonTypeInfo contract)
    {
        var members = new JsonArray();

        foreach (JsonPropertyInfo property in contract.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            members.Add(DescribeMember(contract, property));
        }

        var described = new JsonObject
        {
            ["typeName"] = TypeName(contract.Type),
            ["kind"] = KindName(contract.Kind),
            ["supported"] = ConverterClassifier.IsSupported(contract),
            ["extensionData"] = contract.Properties.Any(static property => property.IsExtensionData),
            ["members"] = members,
        };

        JsonObject? polymorphism = DescribePolymorphism(contract);

        if (polymorphism is not null)
        {
            described["polymorphism"] = polymorphism;
        }

        return described;
    }

    private static JsonObject DescribeMember(JsonTypeInfo contract, JsonPropertyInfo property)
    {
        string? unsupportedReason = ConverterClassifier.DescribeMemberUnsupported(contract, property);

        var member = new JsonObject
        {
            ["name"] = property.Name,
            ["declaredType"] = TypeName(property.PropertyType),
            ["tokenKind"] = TokenKind(
                property.PropertyType,
                unsupportedReason is not null,
                ConverterClassifier.WritesStringTokens(contract, property)),
            ["required"] = property.IsRequired,
            ["getNullable"] = property.IsGetNullable,
            ["setNullable"] = property.IsSetNullable,
            ["extensionData"] = property.IsExtensionData,
            ["supported"] = unsupportedReason is null,
        };

        JsonObject? shape = DescribeShape(property.PropertyType, contract.Options);

        if (shape is not null)
        {
            member["shape"] = shape;
        }

        return member;
    }

    private static JsonObject? DescribeShape(Type declaredType, JsonSerializerOptions options)
    {
        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (IsScalar(type))
        {
            return null;
        }

        try
        {
            JsonTypeInfo referenced = options.GetTypeInfo(type);

            var shape = new JsonObject
            {
                ["kind"] = KindName(referenced.Kind),
                ["typeName"] = TypeName(type),
                ["memberNames"] = MemberNames(referenced),
                ["supported"] = ConverterClassifier.IsSupported(referenced),
            };

            if (TryDescribeKeyAndValue(type, out Type? keyType, out Type? valueType))
            {
                shape["keyType"] = TypeName(keyType!);
                shape["valueType"] = TypeName(valueType!);
            }
            else if (referenced.ElementType is Type elementType)
            {
                shape["elementType"] = TypeName(elementType);
            }

            return shape;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return new JsonObject
            {
                ["kind"] = "unresolved",
                ["typeName"] = TypeName(type),
                ["reason"] = exception.GetType().Name,
            };
        }
    }

    private static JsonArray MemberNames(JsonTypeInfo contract)
    {
        var names = new JsonArray();

        foreach (string name in contract.Properties
            .Where(static property => !property.IsExtensionData)
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal))
        {
            names.Add(name);
        }

        return names;
    }

    private static JsonObject? DescribePolymorphism(JsonTypeInfo contract)
    {
        JsonPolymorphismOptions? polymorphism = contract.PolymorphismOptions;

        if (polymorphism is null || polymorphism.DerivedTypes.Count == 0)
        {
            return null;
        }

        var derivedTypes = new JsonArray();

        foreach (JsonDerivedType derived in polymorphism.DerivedTypes
            .OrderBy(static derived => derived.TypeDiscriminator?.ToString(), StringComparer.Ordinal))
        {
            derivedTypes.Add(new JsonObject
            {
                ["discriminator"] = derived.TypeDiscriminator?.ToString(),
                ["typeName"] = TypeName(derived.DerivedType),
            });
        }

        return new JsonObject
        {
            ["discriminatorPropertyName"] = polymorphism.TypeDiscriminatorPropertyName,
            ["unknownDerivedTypeHandling"] = polymorphism.UnknownDerivedTypeHandling.ToString(),
            ["derivedTypes"] = derivedTypes,
        };
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type.IsPointer ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(DateOnly) ||
        type == typeof(TimeOnly) ||
        type == typeof(TimeSpan) ||
        type == typeof(Uri) ||
        type == typeof(object) ||
        type == typeof(JsonElement) ||
        type == typeof(JsonDocument) ||
        type == typeof(JsonNode);

    private static bool TryDescribeKeyAndValue(Type type, out Type? keyType, out Type? valueType)
    {
        keyType = null;
        valueType = null;

        foreach (Type candidate in new[] { type }.Concat(type.GetInterfaces()))
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            Type definition = candidate.GetGenericTypeDefinition();

            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                Type[] arguments = candidate.GetGenericArguments();
                keyType = arguments[0];
                valueType = arguments[1];
                return true;
            }
        }

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();

            if (definition == typeof(Dictionary<,>) || definition == typeof(SortedDictionary<,>))
            {
                Type[] arguments = type.GetGenericArguments();
                keyType = arguments[0];
                valueType = arguments[1];
                return true;
            }
        }

        return false;
    }

    private static string TokenKind(Type declaredType, bool unsupported, bool writesStringTokens)
    {
        if (unsupported)
        {
            return "opaque";
        }

        if (writesStringTokens)
        {
            return "string";
        }

        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(byte[]))
        {
            return "string";
        }

        if (type == typeof(string) ||
            type == typeof(char) ||
            type == typeof(Guid) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(DateOnly) ||
            type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) ||
            type == typeof(Uri))
        {
            return "string";
        }

        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonNode) || type == typeof(JsonDocument))
        {
            return "any";
        }

        if (TryDescribeKeyAndValue(type, out _, out _))
        {
            return "object";
        }

        if (IsEnumerable(type))
        {
            return "array";
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal))
        {
            return "number";
        }

        return "object";
    }

    private static bool IsEnumerable(Type type) =>
        type != typeof(string) &&
        (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || type.IsArray);

    private static string KindName(JsonTypeInfoKind kind) => kind switch
    {
        JsonTypeInfoKind.Object => "object",
        JsonTypeInfoKind.Enumerable => "array",
        JsonTypeInfoKind.Dictionary => "dictionary",
        JsonTypeInfoKind.None => "scalar",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string TypeName(Type type)
    {
        if (type.IsArray)
        {
            return string.Concat(TypeName(type.GetElementType()!), "[]");
        }

        if (type.IsGenericType)
        {
            string definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
            int marker = definition.IndexOf('`', StringComparison.Ordinal);

            if (marker >= 0)
            {
                definition = definition[..marker];
            }

            string arguments = string.Join(",", type.GetGenericArguments().Select(TypeName));
            return $"{definition}<{arguments}>";
        }

        return type.FullName ?? type.Name;
    }
}
