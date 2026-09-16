namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Builds rule-matrix checks from executed observations.
/// </summary>
internal static class Check
{
    public static CheckOutcome Compatible(string id, string title, string change, ReadOutcome outcome) =>
        FromReading(id, title, change, Display("ReaderBackward", true), outcome.Lossless, "ReaderBackward", outcome);

    public static CheckOutcome Incompatible(string id, string title, string change, ReadOutcome outcome) =>
        FromReading(id, title, change, Display("ReaderBackward", false), !outcome.Lossless, "ReaderBackward", outcome);

    public static CheckOutcome Supported(string id, string title, string change, string? unsupportedReason) =>
        new(
            id,
            title,
            change,
            "metadata=Supported",
            unsupportedReason is null ? "metadata=Supported" : "metadata=Unsupported",
            unsupportedReason is null ? "no unrecognized converter metadata" : unsupportedReason,
            unsupportedReason is null);

    public static CheckOutcome Unsupported(string id, string title, string change, string? unsupportedReason) =>
        new(
            id,
            title,
            change,
            "metadata=Unsupported",
            unsupportedReason is null ? "metadata=Supported" : "metadata=Unsupported",
            unsupportedReason ?? "no unrecognized converter metadata",
            unsupportedReason is not null);

    public static CheckOutcome Assert(
        string id,
        string title,
        string change,
        string expected,
        string measured,
        string observation,
        bool passed) =>
        new(id, title, change, expected, measured, observation, passed);

    /// <summary>
    /// Emits the classification of one change under all three candidate policies, measured in both
    /// directions: the later contract reading what the earlier contract wrote, and the earlier contract
    /// reading what the later contract wrote.
    /// </summary>
    public static IReadOnlyList<CheckOutcome> Classify(
        string id,
        string title,
        string change,
        ReadOutcome readerBackward,
        bool readerBackwardCompatible,
        ReadOutcome writerForward,
        bool writerForwardCompatible)
    {
        bool measuredReaderBackward = readerBackward.Lossless;
        bool measuredWriterForward = writerForward.Lossless;
        bool measuredFull = measuredReaderBackward && measuredWriterForward;
        bool expectedFull = readerBackwardCompatible && writerForwardCompatible;

        return new[]
        {
            FromReading(
                id,
                title,
                change,
                Display("ReaderBackward", readerBackwardCompatible),
                measuredReaderBackward == readerBackwardCompatible,
                "ReaderBackward",
                readerBackward),
            FromReading(
                $"{id}.forward",
                title,
                change,
                Display("WriterForward", writerForwardCompatible),
                measuredWriterForward == writerForwardCompatible,
                "WriterForward",
                writerForward),
            new CheckOutcome(
                $"{id}.full",
                title,
                change,
                Display("Full", expectedFull),
                Display("Full", measuredFull),
                $"readerBackward: {Describe(readerBackward)} | writerForward: {Describe(writerForward)}",
                measuredFull == expectedFull),
        };
    }

    public static string Describe(ReadOutcome outcome)
    {
        var parts = new List<string>
        {
            $"parsed={outcome.Parsed}",
        };

        if (outcome.Parsed)
        {
            parts.Add($"lossless={outcome.Lossless}");
        }
        else
        {
            parts.Add($"error={Truncate(outcome.Failure, 120)}");
        }

        if (outcome.Differences.Count > 0)
        {
            parts.Add($"lost=[{string.Join(", ", outcome.Differences)}]");
        }

        parts.Add($"document={Truncate(outcome.Document, 160)}");

        if (outcome.Parsed)
        {
            parts.Add($"rebound={Truncate(outcome.ReboundedDocument, 160)}");
        }

        return string.Join("; ", parts);
    }

    public static string Truncate(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= limit ? value : string.Concat(value.AsSpan(0, limit), "...");
    }

    private static string Display(string policy, bool compatible) =>
        $"{policy}={(compatible ? "Compatible" : "Incompatible")}";

    private static CheckOutcome FromReading(
        string id,
        string title,
        string change,
        string expected,
        bool passed,
        string policy,
        ReadOutcome outcome) =>
        new(
            id,
            title,
            change,
            expected,
            Display(policy, outcome.Lossless),
            Describe(outcome),
            passed);
}
