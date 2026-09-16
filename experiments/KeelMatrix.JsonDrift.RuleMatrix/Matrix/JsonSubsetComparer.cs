using System.Globalization;
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
    /// Numbers are compared by exact value, so a change of serialized form that carries the same number
    /// (<c>5</c> written as <c>5.0</c>, <c>1e2</c> written as <c>100</c>) is not reported as a loss, while a
    /// difference in value is reported however the number was written. Every other token is compared by
    /// serialized form.
    /// </summary>
    private static bool ValueEquals(JsonElement earlier, JsonElement later) =>
        earlier.ValueKind == JsonValueKind.Number && later.ValueKind == JsonValueKind.Number
            ? ExactValueEquals(earlier.GetRawText(), later.GetRawText())
            : string.Equals(earlier.GetRawText(), later.GetRawText(), StringComparison.Ordinal);

    /// <summary>
    /// Compares two written JSON numbers exactly. <see cref="ExactValue"/> reduces a written number to its
    /// sign, its significant digits, and the decimal exponent of its last significant digit, so equality
    /// means that both forms denote the same mathematical value. The comparison is exact for every JSON
    /// number, including values outside the range of <see cref="decimal"/> and of a finite
    /// <see cref="double"/>: a magnitude difference that a rounded or non-finite parse would collapse is
    /// therefore reported instead of hidden.
    /// </summary>
    private static bool ExactValueEquals(string earlier, string later)
    {
        string? earlierValue = ExactValue(earlier);
        string? laterValue = ExactValue(later);

        if (earlierValue is null || laterValue is null)
        {
            return string.Equals(earlier, later, StringComparison.Ordinal);
        }

        return string.Equals(earlierValue, laterValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact canonical form of a written JSON number: the optional sign, the significant digits with
    /// leading and trailing zeros removed, and the decimal exponent of the digits. Zero is recorded as
    /// <c>0</c>. A form that cannot be represented exactly is reported as null, and the comparison then falls
    /// back to the serialized text.
    /// </summary>
    private static string? ExactValue(string text)
    {
        int index = 0;
        bool negative = false;

        if (text.StartsWith('-'))
        {
            negative = true;
            index = 1;
        }

        var digits = new List<char>();
        int digitsBeforePoint = 0;
        long exponent = 0;
        bool afterPoint = false;

        for (; index < text.Length; index++)
        {
            char character = text[index];

            if (character == '.')
            {
                afterPoint = true;
                continue;
            }

            if (character is 'e' or 'E')
            {
                if (!long.TryParse(
                    text[(index + 1)..],
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out exponent))
                {
                    return null;
                }

                break;
            }

            if (character is < '0' or > '9')
            {
                return null;
            }

            digits.Add(character);

            if (!afterPoint)
            {
                digitsBeforePoint++;
            }
        }

        int leading = 0;

        while (leading < digits.Count && digits[leading] == '0')
        {
            leading++;
        }

        if (leading == digits.Count)
        {
            return "0";
        }

        int trailing = digits.Count;

        while (trailing > leading && digits[trailing - 1] == '0')
        {
            trailing--;
        }

        long scale = exponent + digitsBeforePoint - trailing;
        string significant = new(digits.GetRange(leading, trailing - leading).ToArray());

        return $"{(negative ? "-" : string.Empty)}{significant}e{scale}";
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => $"array[{element.GetArrayLength()}]",
        JsonValueKind.Object => "object",
        _ => $"{element.ValueKind.ToString().ToLowerInvariant()} {element.GetRawText()}",
    };
}
