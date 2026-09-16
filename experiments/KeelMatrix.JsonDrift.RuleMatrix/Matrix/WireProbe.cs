using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// The measured outcome of reading a document with a contract and writing it back with that same contract.
/// <paramref name="Fault"/> carries an unexpected exception: a probe that cannot run is neither a compatible
/// nor an incompatible observation, and every check built on it must fail instead of passing.
/// </summary>
internal sealed record ReadOutcome(
    bool Parsed,
    bool ValuesPreserved,
    IReadOnlyList<string> Differences,
    string? Failure,
    string Document,
    string ReboundedDocument,
    string? Fault)
{
    /// <summary>
    /// A reading is lossless when the later contract accepts the document without an exception and every
    /// member the earlier contract wrote is still observable in what the later contract writes back.
    /// </summary>
    public bool Lossless => Parsed && ValuesPreserved;
}

/// <summary>
/// Executes real <see cref="System.Text.Json"/> serialization and deserialization for one contract pair.
/// A rejection by the contract is recorded as a reading; any other exception is recorded as a probe fault
/// so an unexpected converter or resolver exception fails a check instead of terminating the run.
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

            return new ReadOutcome(true, differences.Count == 0, differences, null, document, rebounded, null);
        }
        catch (JsonException exception)
        {
            return Rejected(document, exception);
        }
        catch (NotSupportedException exception)
        {
            return Rejected(document, exception);
        }
        catch (InvalidOperationException exception)
        {
            return Rejected(document, exception);
        }
        catch (Exception exception)
        {
            return Faulted(document, exception, "reading the document");
        }
    }

    /// <summary>
    /// Writes a value with the earlier contract and reads the resulting document with the later contract.
    /// </summary>
    public static ReadOutcome Across(object? earlierValue, JsonTypeInfo earlierContract, JsonTypeInfo laterContract)
    {
        string document;

        try
        {
            document = Write(earlierValue, earlierContract);
        }
        catch (Exception exception)
        {
            return Faulted(string.Empty, exception, "writing the earlier document");
        }

        return Read(document, laterContract);
    }

    public static string Summarize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string message = exception.Message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return $"{exception.GetType().Name}: {message}";
    }

    /// <summary>
    /// A faulted probe: no document was produced or read, so the observation cannot support any
    /// classification.
    /// </summary>
    public static ReadOutcome Faulted(string document, Exception exception, string stage) =>
        new(false, false, Array.Empty<string>(), null, document, string.Empty, $"{stage}: {Summarize(exception)}");

    private static ReadOutcome Rejected(string document, Exception exception) =>
        new(false, false, Array.Empty<string>(), Summarize(exception), document, string.Empty, null);
}
