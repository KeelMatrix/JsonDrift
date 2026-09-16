using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// The measured outcome of reading a document with a contract and writing it back with that same contract.
/// </summary>
internal sealed record ReadOutcome(
    bool Parsed,
    bool ValuesPreserved,
    IReadOnlyList<string> Differences,
    string? Failure,
    string Document,
    string ReboundedDocument)
{
    /// <summary>
    /// A reading is lossless when the later contract accepts the document without an exception and every
    /// member the earlier contract wrote is still observable in what the later contract writes back.
    /// </summary>
    public bool Lossless => Parsed && ValuesPreserved;
}

/// <summary>
/// Executes real <see cref="System.Text.Json"/> serialization and deserialization for one contract pair.
/// </summary>
internal static class WireProbe
{
    public static string Write(object? value, JsonTypeInfo contract) => JsonSerializer.Serialize(value, contract);

    public static ReadOutcome Read(string document, JsonTypeInfo contract)
    {
        try
        {
            object? bound = JsonSerializer.Deserialize(document, contract);
            string rebounded = JsonSerializer.Serialize(bound, contract);
            IReadOnlyList<string> differences = JsonSubsetComparer.Compare(document, rebounded);

            return new ReadOutcome(true, differences.Count == 0, differences, null, document, rebounded);
        }
        catch (JsonException exception)
        {
            return new ReadOutcome(false, false, Array.Empty<string>(), Summarize(exception), document, string.Empty);
        }
        catch (NotSupportedException exception)
        {
            return new ReadOutcome(false, false, Array.Empty<string>(), Summarize(exception), document, string.Empty);
        }
        catch (InvalidOperationException exception)
        {
            return new ReadOutcome(false, false, Array.Empty<string>(), Summarize(exception), document, string.Empty);
        }
    }

    /// <summary>
    /// Writes a value with the earlier contract and reads the resulting document with the later contract.
    /// </summary>
    public static ReadOutcome Across(object? earlierValue, JsonTypeInfo earlierContract, JsonTypeInfo laterContract) =>
        Read(Write(earlierValue, earlierContract), laterContract);

    public static string Summarize(Exception exception)
    {
        string message = exception.Message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return $"{exception.GetType().Name}: {message}";
    }
}
