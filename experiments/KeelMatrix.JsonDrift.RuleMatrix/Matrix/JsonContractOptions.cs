using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Creates the serializer option sets used by the rule matrix.
/// </summary>
internal static class JsonContractOptions
{
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
}
