namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// One executed rule-matrix check: what was claimed, what was observed, whether the two agree.
/// </summary>
internal sealed record CheckOutcome(
    string Id,
    string Title,
    string Change,
    string Expected,
    string Measured,
    string Observation,
    bool Passed);
