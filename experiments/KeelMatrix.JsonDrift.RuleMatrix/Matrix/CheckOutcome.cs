namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// One executed rule-matrix check: what was claimed, what was observed, whether the two agree.
/// <paramref name="SourceId"/> and <paramref name="PathId"/> are set only by checks that reach opaque
/// metadata: they name the discovery source the check exercised and the inventory check that documents it,
/// so the executed checks can be compared with the path inventory without parsing printed text.
/// </summary>
internal sealed record CheckOutcome(
    string Id,
    string Title,
    string Change,
    string Expected,
    string Measured,
    string Observation,
    bool Passed,
    string? SourceId = null,
    string? PathId = null);
