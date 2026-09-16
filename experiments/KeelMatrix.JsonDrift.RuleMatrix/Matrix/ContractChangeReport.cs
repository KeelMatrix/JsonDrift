namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Experiment-local stand-in for the eventual comparison report and assertion. It exists to prove that a
/// contract whose metadata cannot be classified never produces a green result, even when every other change
/// in the same run is compatible.
/// </summary>
internal sealed class ContractChangeReport
{
    private readonly List<(string Id, string Classification, string Reason)> changes = new();

    public void AddCompatible(string id, string reason) => changes.Add((id, "Compatible", reason));

    public void AddIncompatible(string id, string reason) => changes.Add((id, "Incompatible", reason));

    public void AddUnsupported(string id, string reason) => changes.Add((id, "Unsupported", reason));

    public string Status =>
        changes.Any(change => change.Classification == "Unsupported") ? "Unsupported"
        : changes.Any(change => change.Classification == "Incompatible") ? "Incompatible"
        : "Compatible";

    public IReadOnlyList<string> Describe() =>
        changes.Select(change => $"{change.Id}: {change.Classification} ({change.Reason})").ToArray();

    public void AssertCompatible()
    {
        if (Status != "Compatible")
        {
            throw new ContractCheckFailedException(
                $"contract comparison is not compatible: {Status}. {string.Join("; ", Describe())}");
        }
    }
}

internal sealed class ContractCheckFailedException : Exception
{
    public ContractCheckFailedException(string message)
        : base(message)
    {
    }
}
