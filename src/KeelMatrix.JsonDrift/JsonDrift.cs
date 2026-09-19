using System.Text.Json;
using System.Text.Json.Serialization;
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
        return JsonContract.FromCanonicalJson(
            bytes,
            JsonBaselineLimits.Default,
            usesSourceGeneratedMetadata: contract.OriginatingResolver is JsonSerializerContext);
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

    /// <summary>Compares the current metadata with a canonical baseline at <paramref name="baselinePath"/>.</summary>
    /// <param name="contract">The later contract metadata used by the application.</param>
    /// <param name="baselinePath">The local path of the earlier accepted baseline.</param>
    /// <param name="compatibility">The supported compatibility policy.</param>
    /// <returns>A complete structured comparison report.</returns>
    public static JsonDriftReport Compare(JsonTypeInfo contract, string baselinePath, JsonCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath);
        return Compare(Extract(contract), JsonBaseline.Read(baselinePath), compatibility);
    }

    /// <summary>Compares current metadata with an already-extracted earlier contract.</summary>
    /// <param name="contract">The later contract metadata used by the application.</param>
    /// <param name="baseline">The earlier accepted contract.</param>
    /// <param name="compatibility">The supported compatibility policy.</param>
    /// <returns>A complete structured comparison report.</returns>
    public static JsonDriftReport Compare(JsonTypeInfo contract, JsonContract baseline, JsonCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(baseline);
        return Compare(Extract(contract), baseline, compatibility);
    }

    /// <summary>Compares current options metadata with a baseline at <paramref name="baselinePath"/>.</summary>
    public static JsonDriftReport Compare<T>(JsonSerializerOptions options, string baselinePath, JsonCompatibility compatibility) =>
        Compare(typeof(T), options, baselinePath, compatibility);

    /// <summary>Compares current options metadata with an already-extracted baseline.</summary>
    public static JsonDriftReport Compare<T>(JsonSerializerOptions options, JsonContract baseline, JsonCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(baseline);
        return Compare(Extract<T>(options), baseline, compatibility);
    }

    internal static JsonDriftReport Compare(JsonContract contract, JsonContract baseline, JsonCompatibility compatibility)
    {
        JsonDriftReport report = Internal.ContractComparison.Compare(baseline, contract, compatibility);
        Internal.JsonDriftTelemetryCoordinator.RecordComparison();
        return report;
    }

    private static JsonDriftReport Compare(Type type, JsonSerializerOptions options, string baselinePath, JsonCompatibility compatibility) =>
        Compare(Extract(type, options), JsonBaseline.Read(baselinePath), compatibility);
}
