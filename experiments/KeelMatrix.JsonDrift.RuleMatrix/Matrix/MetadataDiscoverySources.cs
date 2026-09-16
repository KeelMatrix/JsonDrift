using System.Diagnostics.CodeAnalysis;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// The recorded path inventory. The implemented discovery sources are the sources the traversal actually
/// visited, read from the runtime inventory rather than from a hand-maintained list, so a source that is
/// declared and handled but never visited by an executed contract is visible as a missing entry. The
/// inventory is compared with the documented path table and with the executed per-path checks.
/// </summary>
internal static class MetadataDiscoverySources
{
    /// <summary>
    /// The discovery sources the recorded traversals visited, derived from the walk itself. A source that the
    /// walk never reaches is absent, which fails the path-inventory check instead of passing by default.
    /// </summary>
    public static IReadOnlyList<string> Implemented =>
        TraversalInventory.Ledger.VisitedSources()
            .Select(MetadataSourceRules.Id)
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Tags an unsupported reason with the discovery source that recorded the fact, so a check can report
    /// which path produced the verdict. The tag is removed by <see cref="Reason"/>.
    /// </summary>
    public static string Witness(string source, string reason) => $"[{source}] {reason}";

    /// <summary>The reported reason without its source tag.</summary>
    public static string Reason(string witness)
    {
        ArgumentNullException.ThrowIfNull(witness);

        if (witness.Length > 2 && witness[0] == '[')
        {
            int end = witness.IndexOf(']', StringComparison.Ordinal);

            if (end > 0)
            {
                return witness[(end + 1)..].Trim();
            }
        }

        return witness;
    }

    /// <summary>
    /// Parses one row of the path inventory table, of the form
    /// <c>| `source-id` | `check-id` | description |</c>.
    /// </summary>
    public static bool TryParseInventoryEntry(
        string line,
        [NotNullWhen(true)] out string? source,
        [NotNullWhen(true)] out string? checkId)
    {
        source = null;
        checkId = null;

        string[]? cells = SplitTableRow(line);

        if (cells is null || cells.Length < 2)
        {
            return false;
        }

        string first = TrimCode(cells[0]);
        string second = TrimCode(cells[1]);

        if (first.Length == 0 ||
            second.Length == 0 ||
            !first.Contains('-', StringComparison.Ordinal) ||
            !second.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        source = first;
        checkId = second;
        return true;
    }

    /// <summary>
    /// Splits a Markdown table row into its cells, or returns null when the line is not a table row.
    /// </summary>
    public static string[]? SplitTableRow(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string trimmed = line.Trim();

        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
        {
            return null;
        }

        string[] cells = trimmed[1..^1].Split('|');

        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = cells[index].Trim();
        }

        return cells;
    }

    /// <summary>
    /// The discovery sources that registered a contract whose metadata is opaque, read from the executed
    /// checks rather than from the walker's own bookkeeping.
    /// </summary>
    public static IReadOnlySet<string> ObservedFromChecks(IEnumerable<CheckOutcome> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var observed = new HashSet<string>(StringComparer.Ordinal);

        foreach (CheckOutcome check in checks)
        {
            if (check.SourceId is not null)
            {
                observed.Add(check.SourceId);
            }
        }

        return observed;
    }

    /// <summary>
    /// The bindings carried by the checks: each executed path records the discovery source it exercised and
    /// the inventory check that documents it. Every distinct pair is reported, so a source exercised by
    /// several checks keeps every binding and the inventory row may name any one of them.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> InventoryBindings(IEnumerable<CheckOutcome> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        return checks
            .Where(static check => check.SourceId is not null && check.PathId is not null)
            .Select(static check => new KeyValuePair<string, string>(check.SourceId!, check.PathId!))
            .Distinct()
            .OrderBy(static binding => binding.Key, StringComparer.Ordinal)
            .ThenBy(static binding => binding.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string TrimCode(string cell) => cell.Trim().Trim('`').Trim();
}
