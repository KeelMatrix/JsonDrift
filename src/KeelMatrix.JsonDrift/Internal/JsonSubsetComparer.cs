using System.Text.Json;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>Compares earlier JSON members against a later document for the lossless wire probes.</summary>
internal static class WireSubsetComparer
{
    public static IReadOnlyList<string> Compare(string earlierDocument, string laterDocument)
    {
        var differences = new List<string>();
        using JsonDocument earlier = JsonDocument.Parse(earlierDocument);
        using JsonDocument later = JsonDocument.Parse(laterDocument);
        CompareElement(earlier.RootElement, later.RootElement, "$", differences);
        return differences;
    }

    private static void CompareElement(JsonElement earlier, JsonElement later, string path, List<string> differences)
    {
        if (earlier.ValueKind != later.ValueKind)
        {
            differences.Add($"{path} ({earlier.ValueKind} -> {later.ValueKind})");
            return;
        }

        switch (earlier.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in earlier.EnumerateObject())
                {
                    if (!later.TryGetProperty(property.Name, out JsonElement laterValue))
                    {
                        differences.Add($"{path}.{property.Name} (missing)");
                    }
                    else
                    {
                        CompareElement(property.Value, laterValue, $"{path}.{property.Name}", differences);
                    }
                }

                break;

            case JsonValueKind.Array:
                JsonElement[] earlierItems = earlier.EnumerateArray().ToArray();
                JsonElement[] laterItems = later.EnumerateArray().ToArray();
                if (earlierItems.Length != laterItems.Length)
                {
                    differences.Add($"{path} (array length {earlierItems.Length} -> {laterItems.Length})");
                }
                else
                {
                    for (int index = 0; index < earlierItems.Length; index++)
                    {
                        CompareElement(earlierItems[index], laterItems[index], $"{path}[{index}]", differences);
                    }
                }

                break;

            default:
                if (!ValuesEqual(earlier, later))
                {
                    differences.Add($"{path} ({earlier.GetRawText()} -> {later.GetRawText()})");
                }

                break;
        }
    }

    private static bool ValuesEqual(JsonElement earlier, JsonElement later) =>
        earlier.ValueKind == JsonValueKind.Number && later.ValueKind == JsonValueKind.Number
            ? decimal.TryParse(earlier.GetRawText(), out decimal left) &&
              decimal.TryParse(later.GetRawText(), out decimal right) && left == right
            : string.Equals(earlier.GetRawText(), later.GetRawText(), StringComparison.Ordinal);
}
