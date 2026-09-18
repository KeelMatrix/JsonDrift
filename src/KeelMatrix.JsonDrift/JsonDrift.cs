using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift;

/// <summary>Extracts a canonical structural contract from effective System.Text.Json metadata.</summary>
public static class JsonDrift
{
    /// <summary>Extracts a contract from an already selected <see cref="JsonTypeInfo"/>.</summary>
    /// <param name="contract">The effective metadata used by the application.</param>
    /// <returns>The deterministic contract model.</returns>
    public static JsonContract Extract(JsonTypeInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        string canonicalJson = Internal.ContractCanonicalizer.Canonicalize(contract);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(canonicalJson);
        return JsonContract.FromCanonicalJson(bytes, JsonBaselineLimits.Default);
    }

    /// <summary>Extracts a contract for <paramref name="type"/> from the supplied serializer options.</summary>
    /// <param name="type">The root CLR type to resolve.</param>
    /// <param name="options">The actual serializer options used by the application.</param>
    /// <returns>The deterministic contract model.</returns>
    public static JsonContract Extract(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        return Extract(options.GetTypeInfo(type));
    }

    /// <summary>Extracts a contract for <typeparamref name="T"/> from the supplied serializer options.</summary>
    /// <typeparam name="T">The root CLR type to resolve.</typeparam>
    /// <param name="options">The actual serializer options used by the application.</param>
    /// <returns>The deterministic contract model.</returns>
    public static JsonContract Extract<T>(JsonSerializerOptions options) => Extract(typeof(T), options);
}
