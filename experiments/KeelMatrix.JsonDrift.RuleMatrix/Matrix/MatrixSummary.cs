namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Derives the counts the product documentation quotes from the matrix output itself. A rule that is added,
/// removed, or reclassified changes a measured count, and the check fails until the recorded number and the
/// documentation are updated together, so a hand-written count cannot drift from the evidence again.
/// </summary>
internal static class MatrixSummary
{
    /// <summary>Checks this class contributes, plus the coverage checks that run after it.</summary>
    public const int SummaryCheckCount = 9;

    /// <summary>Executed checks in the matrix, asserted by <c>D04.matrix.check-count</c>.</summary>
    public const int ExpectedCheckCount = 225;

    /// <summary>Measured changes compatible under both <c>ReaderBackward</c> and <c>WriterForward</c>.</summary>
    public const int ExpectedFullCompatibleCount = 6;

    /// <summary>Executed checks that report unsupported converter metadata.</summary>
    public const int ExpectedUnsupportedCount = 62;

    public static IEnumerable<CheckOutcome> Run(IReadOnlyList<CheckOutcome> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        int total = checks.Count + SummaryCheckCount;
        string[] fullCompatible = Ids(checks, "Full=Compatible");
        string[] unsupported = checks
            .Where(check => check.Measured.Contains("metadata=Unsupported", StringComparison.Ordinal))
            .Select(check => check.Id)
            .OrderBy(check => check, StringComparer.Ordinal)
            .ToArray();

        yield return Check.Assert(
            "D04.matrix.check-count",
            "Matrix summary",
            "the matrix runs the recorded number of executed checks",
            $"checks={ExpectedCheckCount}",
            $"checks={total}",
            $"fullCompatible={fullCompatible.Length}; unsupported={unsupported.Length}",
            total == ExpectedCheckCount);

        yield return Check.Assert(
            "D04.policy.full-compatible-count",
            "Matrix summary",
            "the measured changes that are compatible under every recorded policy are counted from the matrix output",
            $"Full=Compatible={ExpectedFullCompatibleCount}",
            $"Full=Compatible={fullCompatible.Length}",
            $"ids=[{string.Join(", ", fullCompatible)}]",
            fullCompatible.Length == ExpectedFullCompatibleCount);

        yield return Check.Assert(
            "D04.policy.unsupported-count",
            "Matrix summary",
            "the contracts reported as unsupported are counted from the matrix output",
            $"metadata=Unsupported={ExpectedUnsupportedCount}",
            $"metadata=Unsupported={unsupported.Length}",
            $"ids=[{string.Join(", ", unsupported)}]",
            unsupported.Length == ExpectedUnsupportedCount);
    }

    private static string[] Ids(IReadOnlyList<CheckOutcome> checks, string measured) =>
        checks.Where(check => string.Equals(check.Measured, measured, StringComparison.Ordinal))
            .Select(check => check.Id)
            .OrderBy(check => check, StringComparer.Ordinal)
            .ToArray();
}
