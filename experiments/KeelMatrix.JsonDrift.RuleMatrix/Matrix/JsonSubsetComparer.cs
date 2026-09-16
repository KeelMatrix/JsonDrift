using System.Text.Json;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Compares the members of an earlier document against the document a later contract produced from it.
/// Members that the earlier contract wrote and the later contract no longer preserves are reported as lost.
/// Members the later contract adds are not losses: they are new data.
/// </summary>
internal static class JsonSubsetComparer
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
            differences.Add($"{path} ({Describe(earlier)} -> {Describe(later)})");
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
                        continue;
                    }

                    CompareElement(property.Value, laterValue, $"{path}.{property.Name}", differences);
                }

                break;

            case JsonValueKind.Array:
                JsonElement[] earlierItems = earlier.EnumerateArray().ToArray();
                JsonElement[] laterItems = later.EnumerateArray().ToArray();

                if (earlierItems.Length != laterItems.Length)
                {
                    differences.Add($"{path} ({Describe(earlier)} -> {Describe(later)})");
                    break;
                }

                for (int i = 0; i < earlierItems.Length; i++)
                {
                    CompareElement(earlierItems[i], laterItems[i], $"{path}[{i}]", differences);
                }

                break;

            default:
                if (!ValueEquals(earlier, later))
                {
                    differences.Add($"{path} ({Describe(earlier)} -> {Describe(later)})");
                }

                break;
        }
    }

    /// <summary>
    /// Numbers are compared by value, so a change of serialized form that carries the same number
    /// (<c>5</c> written as <c>5.0</c>) is not reported as a loss. A parse only decides equality while it is
    /// lossless for both sides: a decimal parse that collapses a non-zero value to zero, and a non-finite
    /// double parse, fall back to serialized text so that a magnitude difference is never hidden. Every
    /// other token is compared by serialized form.
    /// </summary>
    private static bool ValueEquals(JsonElement earlier, JsonElement later) =>
        earlier.ValueKind == JsonValueKind.Number && later.ValueKind == JsonValueKind.Number
            ? NumbersEqual(earlier, later)
            : string.Equals(earlier.GetRawText(), later.GetRawText(), StringComparison.Ordinal);

    private static bool NumbersEqual(JsonElement earlier, JsonElement later)
    {
        if (earlier.TryGetDecimal(out decimal earlierDecimal) &&
            later.TryGetDecimal(out decimal laterDecimal) &&
            IsLosslessZero(earlier, earlierDecimal == 0m) &&
            IsLosslessZero(later, laterDecimal == 0m))
        {
            return earlierDecimal == laterDecimal;
        }

        if (earlier.TryGetDouble(out double earlierDouble) &&
            later.TryGetDouble(out double laterDouble) &&
            double.IsFinite(earlierDouble) &&
            double.IsFinite(laterDouble) &&
            IsLosslessZero(earlier, earlierDouble == 0d) &&
            IsLosslessZero(later, laterDouble == 0d))
        {
            return earlierDouble == laterDouble;
        }

        return string.Equals(earlier.GetRawText(), later.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A numeric parse is only trusted while it did not collapse a non-zero magnitude to zero, which is how
    /// a value below the representable range is reported.
    /// </summary>
    private static bool IsLosslessZero(JsonElement element, bool parsedZero) =>
        !parsedZero || !ContainsNonZeroDigit(element.GetRawText());

    private static bool ContainsNonZeroDigit(string rawText)
    {
        foreach (char character in rawText)
        {
            if (character is >= '1' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => $"array[{element.GetArrayLength()}]",
        JsonValueKind.Object => "object",
        _ => $"{element.ValueKind.ToString().ToLowerInvariant()} {element.GetRawText()}",
    };
}
