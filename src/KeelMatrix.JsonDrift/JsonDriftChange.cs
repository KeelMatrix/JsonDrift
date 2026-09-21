namespace KeelMatrix.JsonDrift;

/// <summary>Describes one ordered structural change found during a contract comparison.</summary>
public sealed class JsonDriftChange
{
    internal JsonDriftChange(
        string path,
        JsonDriftClassification classification,
        string ruleId,
        string reason)
    {
        Path = path;
        Classification = classification;
        RuleId = ruleId;
        Reason = reason;
    }

    /// <summary>Gets the canonical structural path or member associated with the change.</summary>
    public string Path { get; }

    /// <summary>Gets the explicit compatibility classification of the change.</summary>
    public JsonDriftClassification Classification { get; }

    /// <summary>Gets the stable rule identifier that classified the change, or the fail-closed unsupported rule.</summary>
    public string RuleId { get; }

    /// <summary>Gets the human-readable reason for the classification.</summary>
    public string Reason { get; }

    /// <summary>Returns a concise developer-facing description of the change.</summary>
    public override string ToString() => $"{Path}: {Classification} ({RuleId}) {Reason}";
}
