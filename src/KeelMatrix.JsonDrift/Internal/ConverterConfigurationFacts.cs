using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// Derives the observable configuration of an allowlisted enum converter by executing it. No converter
/// private state is read: the integer-token fact is the result of deserializing a representative integer
/// token through the effective enum metadata.
/// </summary>
internal static class ConverterConfigurationFacts
{
    public const string DefaultConverter = "default-enum-converter";
    public const string Unresolved = "<unresolved>";

    public static RecordedConverterConfiguration? Probe(
        JsonSerializerOptions options,
        Type enumType,
        Type? declaredConverterType = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(enumType);

        Type? converterType = declaredConverterType ?? RegisteredEnumConverter(options, enumType);
        bool? integerTokensAccepted = ProbeIntegerToken(options, enumType);

        return integerTokensAccepted is bool accepted
            ? new RecordedConverterConfiguration(
                converterType is null ? DefaultConverter : TypeShapes.TypeName(converterType),
                accepted)
            : new RecordedConverterConfiguration(
                converterType is null ? DefaultConverter : TypeShapes.TypeName(converterType),
                null);
    }

    private static Type? RegisteredEnumConverter(JsonSerializerOptions options, Type enumType)
    {
        foreach (JsonConverter converter in options.Converters)
        {
            if (ContractAllowlists.IsAllowlistedConverterFor(converter.GetType(), enumType))
            {
                return converter.GetType();
            }
        }

        return null;
    }

    private static bool? ProbeIntegerToken(JsonSerializerOptions options, Type enumType)
    {
        try
        {
            Array values = Enum.GetValues(enumType);

            if (values.Length == 0)
            {
                return null;
            }

            object value = values.GetValue(0)!;
            Type underlyingType = Enum.GetUnderlyingType(enumType);
            object numericValue = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture)!;
            string token = Convert.ToString(numericValue, CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException($"could not format enum value for {TypeShapes.TypeName(enumType)}");

            JsonTypeInfo info = options.GetTypeInfo(enumType);
            JsonSerializer.Deserialize(token, info);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>The converter identity and integer-token behavior observed by executing an enum converter.</summary>
internal sealed record RecordedConverterConfiguration(string ConverterType, bool? IntegerTokensAccepted)
{
    public string Display =>
        $"converter={ConverterType},integerTokensAccepted={IntegerTokensAccepted?.ToString().ToLowerInvariant() ?? "<unresolved>"}";
}
