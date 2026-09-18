using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// Type-shape inspection shared by contract classification and canonicalization. Collection, dictionary,
/// and scalar decisions must agree everywhere, otherwise a member that is traversed for converters in one
/// place would be treated as an opaque scalar in another.
/// </summary>
internal static class TypeShapes
{
    public static bool IsEnumerable(Type type) =>
        type != typeof(string) &&
        (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || type.IsArray);

    /// <summary>
    /// The element type of an array or of a generic <see cref="IEnumerable{T}"/> that is not a dictionary.
    /// </summary>
    public static Type? ElementType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type == typeof(string) || TryGetDictionaryTypes(type, out _, out _))
        {
            return null;
        }

        foreach (Type candidate in new[] { type }.Concat(type.GetInterfaces()))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    public static bool TryGetDictionaryTypes(Type type, out Type? keyType, out Type? valueType)
    {
        ArgumentNullException.ThrowIfNull(type);

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

    public static string TypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

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

    /// <summary>
    /// The JSON token kind a framework converter writes for a declared type. The mapping covers the scalar
    /// allowlist, enumerables, dictionaries, and object contracts; a type outside it is recorded as an object
    /// token, which is only reachable for contracts that the allowlist already reports unsupported.
    /// </summary>
    public static string TokenKind(Type declaredType)
    {
        ArgumentNullException.ThrowIfNull(declaredType);

        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(byte[]) ||
            type == typeof(Memory<byte>) ||
            type == typeof(ReadOnlyMemory<byte>) ||
            type == typeof(string) ||
            type == typeof(char) ||
            type == typeof(Guid) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(DateOnly) ||
            type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) ||
            type == typeof(Uri) ||
            type == typeof(Version))
        {
            return "string";
        }

        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonNode) || type == typeof(JsonDocument))
        {
            return "any";
        }

        if (TryGetDictionaryTypes(type, out _, out _))
        {
            return "object";
        }

        if (IsEnumerable(type))
        {
            return "array";
        }

        if (type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(decimal) ||
            type == typeof(Int128) ||
            type == typeof(UInt128) ||
            type == typeof(Half))
        {
            return "number";
        }

        return "object";
    }
}
