namespace KeelMatrix.JsonDrift;

/// <summary>Indicates that a JsonDrift report is incompatible or unsupported.</summary>
public sealed class JsonDriftCompatibilityException : Exception
{
    internal JsonDriftCompatibilityException(JsonDriftReport report)
        : base(CreateMessage(report))
    {
        Report = report;
    }

    /// <summary>Gets the report that caused the assertion to fail.</summary>
    public JsonDriftReport Report { get; }

    private static string CreateMessage(JsonDriftReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        string changes = report.Changes.Count == 0
            ? "no structural changes were recorded"
            : string.Join("; ", report.Changes.Select(static change => change.ToString()));

        return $"JsonDrift compatibility assertion failed with outcome {report.Outcome}: {changes}";
    }
}
