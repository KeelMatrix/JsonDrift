using System.Globalization;
using System.Reflection;
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
    /// <summary>
    /// Recorded when the wire name of a string enum member cannot be produced from the declared metadata.
    /// Such a member is reported unsupported rather than as a classified contract.
    /// </summary>
    private const string UnresolvedToken = "<unresolved>";

    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes one enum member and returns the JSON token it was written as.</summary>
    private delegate string EnumWireWriter(object value);

    public static string Canonicalize(JsonTypeInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var document = new JsonObject
        {
            ["contractVersion"] = 1,
            ["root"] = Describe(contract),
        };

        // The root flag states whether the contract's own metadata is classifiable. The aggregate flag
        // states whether every reachable metadata source is classifiable, so a report layer never has to
        // infer overall safety from a root flag alone.
        document["overallSupported"] = !HasUnsupportedValue(document["root"]!);

        string json = document.ToJsonString(WriterOptions);
        string normalized = json.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : string.Concat(normalized, "\n");
    }

    /// <summary>
    /// True when any record inside the document is marked unsupported, including records for nested shapes,
    /// nested members, enum wire identities, and registered derived types.
    /// </summary>
    private static bool HasUnsupportedValue(JsonNode node)
    {
        if (node is JsonObject value)
        {
            foreach (KeyValuePair<string, JsonNode?> entry in value)
            {
                if (string.Equals(entry.Key, "supported", StringComparison.Ordinal))
                {
                    if (entry.Value is JsonValue flag && flag.TryGetValue(out bool supported) && !supported)
                    {
                        return true;
                    }

                    continue;
                }

                if (entry.Value is not null && HasUnsupportedValue(entry.Value))
                {
                    return true;
                }
            }

            return false;
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is not null && HasUnsupportedValue(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static JsonObject Describe(JsonTypeInfo contract)
    {
        string? unsupportedReason = ConverterClassifier.DescribeUnsupported(contract);
        var members = new JsonArray();

        foreach (JsonPropertyInfo property in contract.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            members.Add(DescribeMember(contract, property));
        }

        var described = new JsonObject
        {
            ["typeName"] = TypeShapes.TypeName(contract.Type),
            ["kind"] = KindName(contract.Kind),
            ["supported"] = unsupportedReason is null,
            ["extensionData"] = contract.Properties.Any(static property => property.IsExtensionData),
            ["members"] = members,
        };

        if (unsupportedReason is null)
        {
            JsonObject? enumWire = DescribeEnumWire(
                contract,
                null,
                contract.Type,
                ConverterClassifier.WritesStringTokens(contract.Options, contract.Type));

            if (enumWire is not null)
            {
                described["enumWire"] = enumWire;

                if (HasUnresolvedToken(enumWire))
                {
                    described["supported"] = false;
                }
            }
        }

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
        bool writesStringTokens = ConverterClassifier.WritesStringTokens(contract, property);

        var member = new JsonObject
        {
            ["name"] = property.Name,
            ["declaredType"] = TypeShapes.TypeName(property.PropertyType),
            ["tokenKind"] = TokenKind(
                property.PropertyType,
                unsupportedReason is not null,
                writesStringTokens),
            ["required"] = property.IsRequired,
            ["getNullable"] = property.IsGetNullable,
            ["setNullable"] = property.IsSetNullable,
            ["extensionData"] = property.IsExtensionData,
            ["supported"] = unsupportedReason is null,
        };

        // An unsupported member has opaque metadata: resolving its shape would execute metadata that the
        // contract cannot classify, so it records no shape at all.
        if (unsupportedReason is not null)
        {
            return member;
        }

        JsonObject? enumWire = DescribeEnumWire(contract, property, property.PropertyType, writesStringTokens);

        if (enumWire is not null)
        {
            member["enumWire"] = enumWire;

            if (HasUnresolvedToken(enumWire))
            {
                member["tokenKind"] = "opaque";
                member["supported"] = false;
                return member;
            }
        }

        JsonObject? shape = DescribeShape(property.PropertyType, contract.Options);

        if (shape is not null)
        {
            member["shape"] = shape;
        }

        return member;
    }

    /// <summary>
    /// Records the wire identity of an enum contract: the serialized name of every member when the applied
    /// framework converter writes strings, or the numeric value of every member otherwise. Without this,
    /// two contracts that differ only in an applied naming policy or in an enum member name produce
    /// identical documents even though their wire values differ.
    /// </summary>
    private static JsonObject? DescribeEnumWire(
        JsonTypeInfo owner,
        JsonPropertyInfo? property,
        Type declaredType,
        bool writesStringTokens)
    {
        Type enumType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (!enumType.IsEnum)
        {
            return null;
        }

        EnumWireWriter? writer = CreateEnumWireWriter(owner, property, enumType);
        var identity = new JsonObject();

        foreach (string name in Enum.GetNames(enumType).OrderBy(static name => name, StringComparer.Ordinal))
        {
            identity[name] = writesStringTokens
                ? StringEnumToken(writer, owner, enumType, name)
                : NumericEnumValue(enumType, name);
        }

        return identity;
    }

    /// <summary>
    /// The converter that decides an enum member's wire name: the member's effective converter first, then
    /// the converter declared on the enum type, then a converter registered on the serializer options. The
    /// member-level declaration takes precedence, so a member-level framework string-enum converter is
    /// recorded as strings even when the contract's options would otherwise write numbers.
    /// </summary>
    private static EnumWireWriter? CreateEnumWireWriter(JsonTypeInfo owner, JsonPropertyInfo? property, Type enumType)
    {
        try
        {
            JsonConverter? memberConverter = property?.CustomConverter;
            JsonSerializerOptions options = memberConverter is not null && memberConverter.Type == enumType
                ? WithConverter(owner.Options, memberConverter)
                : owner.Options;

            return value => WriteEnumToken(value, enumType, options);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The serialized name the member's effective framework string-enum converter produces. The converter
    /// is a framework converter, confirmed by assembly identity, so the serializer itself reports the wire
    /// name including any naming policy. Application converters are never executed: a converter that is not
    /// a framework string-enum converter leaves the wire name unresolved, which is reported as unsupported.
    /// </summary>
    private static string StringEnumToken(
        EnumWireWriter? writer,
        JsonTypeInfo owner,
        Type enumType,
        string name)
    {
        if (writer is null && !ConverterClassifier.WritesStringTokens(owner.Options, enumType))
        {
            return UnresolvedToken;
        }

        try
        {
            object token = Enum.Parse(enumType, name);
            string json = writer is null
                ? JsonSerializer.Serialize(token, owner.Options.GetTypeInfo(enumType))
                : writer(token);

            return json.Length >= 2 && json[0] == '"' && json[^1] == '"' ? json[1..^1] : json;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
        {
            return UnresolvedToken;
        }
    }

    private static JsonSerializerOptions WithConverter(JsonSerializerOptions options, JsonConverter converter)
    {
        var converted = new JsonSerializerOptions(options);
        converted.Converters.Insert(0, converter);
        return converted;
    }

    private static string WriteEnumToken(object token, Type enumType, JsonSerializerOptions options)
    {
        JsonConverter? converter = options.GetConverter(enumType);

        if (converter is null || !MetadataDiscoverySources.IsKnownConverterType(converter.GetType()))
        {
            throw new NotSupportedException($"no framework converter is available for {TypeShapes.TypeName(enumType)}");
        }

        return WriteWithConverter(converter, token);
    }

    private static string WriteWithConverter(JsonConverter converter, object value)
    {
        Type enumType = value.GetType();
        MethodInfo write = typeof(JsonConverter<>)
            .MakeGenericType(enumType)
            .GetMethod(nameof(JsonConverter<int>.Write))!;

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            write.Invoke(converter, new[] { writer, value, (object?)null });
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonValue NumericEnumValue(Type enumType, string name)
    {
        object raw = Enum.Parse(enumType, name);

        return Enum.GetUnderlyingType(enumType) == typeof(ulong)
            ? JsonValue.Create(Convert.ToUInt64(raw, CultureInfo.InvariantCulture))!
            : JsonValue.Create(Convert.ToInt64(raw, CultureInfo.InvariantCulture))!;
    }

    private static bool HasUnresolvedToken(JsonObject enumWire) =>
        enumWire.Any(entry =>
            entry.Value is JsonValue value &&
            value.TryGetValue(out string? token) &&
            string.Equals(token, UnresolvedToken, StringComparison.Ordinal));

    private static JsonObject? DescribeShape(Type declaredType, JsonSerializerOptions options)
    {
        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (TypeShapes.IsScalar(type))
        {
            return null;
        }

        try
        {
            JsonTypeInfo referenced = options.GetTypeInfo(type);

            var shape = new JsonObject
            {
                ["kind"] = KindName(referenced.Kind),
                ["typeName"] = TypeShapes.TypeName(type),
                ["memberNames"] = MemberNames(referenced),
                ["supported"] = ConverterClassifier.IsSupported(referenced),
            };

            if (TypeShapes.TryGetDictionaryTypes(type, out Type? keyType, out Type? valueType))
            {
                shape["keyType"] = TypeShapes.TypeName(keyType!);
                shape["valueType"] = TypeShapes.TypeName(valueType!);
            }
            else if (referenced.ElementType is Type elementType)
            {
                shape["elementType"] = TypeShapes.TypeName(elementType);
            }

            return shape;
        }
        catch (Exception exception)
        {
            return new JsonObject
            {
                ["kind"] = "unresolved",
                ["typeName"] = TypeShapes.TypeName(type),
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
            derivedTypes.Add(DescribeDerivedType(contract, derived));
        }

        return new JsonObject
        {
            ["discriminatorPropertyName"] = polymorphism.TypeDiscriminatorPropertyName,
            ["unknownDerivedTypeHandling"] = polymorphism.UnknownDerivedTypeHandling.ToString(),
            ["derivedTypes"] = derivedTypes,
        };
    }

    /// <summary>
    /// Records a registered derived type with its support state, its members, and its nested shapes, so a
    /// wire-visible change inside a derived type changes the canonical document instead of leaving it
    /// byte-identical. Deriving metadata is recorded without executing any application converter.
    /// </summary>
    private static JsonObject DescribeDerivedType(JsonTypeInfo contract, JsonDerivedType derived)
    {
        var described = new JsonObject
        {
            ["discriminator"] = derived.TypeDiscriminator?.ToString(),
            ["typeName"] = TypeShapes.TypeName(derived.DerivedType),
        };

        JsonTypeInfo? derivedInfo = null;

        try
        {
            derivedInfo = contract.Options.GetTypeInfo(derived.DerivedType);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
        {
            described["supported"] = false;
            described["reason"] = $"{exception.GetType().Name}: derived-type metadata is unavailable";
            return described;
        }

        if (derivedInfo.Kind != JsonTypeInfoKind.Object)
        {
            described["supported"] = true;
            described["kind"] = KindName(derivedInfo.Kind);
            return described;
        }

        string? unsupportedReason = ConverterClassifier.DescribeUnsupported(derivedInfo);
        var members = new JsonArray();

        foreach (JsonPropertyInfo property in derivedInfo.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            members.Add(DescribeMember(derivedInfo, property));
        }

        described["kind"] = KindName(derivedInfo.Kind);
        described["extensionData"] = derivedInfo.Properties.Any(static property => property.IsExtensionData);
        described["supported"] = unsupportedReason is null;
        described["members"] = members;

        return described;
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

        if (TypeShapes.TryGetDictionaryTypes(type, out _, out _))
        {
            return "object";
        }

        if (TypeShapes.IsEnumerable(type))
        {
            return "array";
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal))
        {
            return "number";
        }

        return "object";
    }

    private static string KindName(JsonTypeInfoKind kind) => kind switch
    {
        JsonTypeInfoKind.Object => "object",
        JsonTypeInfoKind.Enumerable => "array",
        JsonTypeInfoKind.Dictionary => "dictionary",
        JsonTypeInfoKind.None => "scalar",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
