namespace KeelMatrix.JsonDrift;

/// <summary>Contains the complete deterministic result of one JsonDrift comparison.</summary>
public sealed class JsonDriftReport
{
    internal JsonDriftReport(JsonDriftClassification outcome, IReadOnlyList<JsonDriftChange> changes)
    {
        Outcome = outcome;
        Changes = changes;
    }

    /// <summary>Gets the aggregate compatibility outcome.</summary>
    public JsonDriftClassification Outcome { get; }

    /// <summary>Gets a value indicating whether the comparison is compatible.</summary>
    public bool IsCompatible => Outcome == JsonDriftClassification.Compatible;

    /// <summary>Gets every ordered change record found by the comparison.</summary>
    public IReadOnlyList<JsonDriftChange> Changes { get; }

    /// <summary>
    /// Throws <see cref="JsonDriftCompatibilityException"/> when the comparison is incompatible or unsupported.
    /// </summary>
    public void AssertCompatible()
    {
        if (!IsCompatible)
        {
            throw new JsonDriftCompatibilityException(this);
        }
    }
}
