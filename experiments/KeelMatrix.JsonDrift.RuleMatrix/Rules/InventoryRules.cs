using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// The path inventory gate: every metadata-resolution path the classifier walks must be named in the
/// registry, documented in the inventory, and covered by an executed check that reports opaque metadata as
/// unsupported. A discovery source that is added without coverage fails here instead of passing silently.
/// </summary>
internal static class InventoryRules
{
    /// <summary>The path-inside-the-document at which the inventory table begins.</summary>
    public const string InventoryHeading = "### Metadata discovery path inventory";

    public static IEnumerable<CheckOutcome> Run(IReadOnlyList<CheckOutcome> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        string? documentPath = LocateDocument();

        if (documentPath is null)
        {
            yield return Check.Assert(
                "D06.discovery-paths.inventory",
                "Metadata discovery inventory",
                "the path inventory is compared with the implemented discovery sources and the executed checks",
                "inventory=readable",
                "inventory=missing",
                $"searched from {AppContext.BaseDirectory} for docs/compatibility-rules.md",
                false);

            yield break;
        }

        string[] lines = File.ReadAllLines(documentPath);
        int headingIndex = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), InventoryHeading, StringComparison.Ordinal));

        var documented = new List<KeyValuePair<string, string>>();

        if (headingIndex >= 0)
        {
            for (int index = headingIndex + 1; index < lines.Length; index++)
            {
                if (lines[index].StartsWith("## ", StringComparison.Ordinal))
                {
                    break;
                }

                if (MetadataDiscoverySources.TryParseInventoryEntry(lines[index], out string? source, out string? checkId))
                {
                    documented.Add(new KeyValuePair<string, string>(source, checkId));
                }
            }
        }

        var implemented = MetadataDiscoverySources.Implemented
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();
        var observed = MetadataDiscoverySources.ObservedFromChecks(checks);
        IReadOnlyList<KeyValuePair<string, string>> bindings = MetadataDiscoverySources.InventoryBindings(checks);

        string[] notObserved = implemented.Where(source => !observed.Contains(source)).ToArray();
        string[] notInRegistry = observed.Where(source => !implemented.Contains(source, StringComparer.Ordinal)).OrderBy(
            static source => source,
            StringComparer.Ordinal).ToArray();
        string[] missingFromInventory = implemented
            .Where(source => !documented.Any(entry => string.Equals(entry.Key, source, StringComparison.Ordinal)))
            .ToArray();
        string[] undocumentedInInventory = documented
            .Where(entry => !implemented.Contains(entry.Key, StringComparer.Ordinal))
            .Select(static entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();
        string[] uncoveredChecks = implemented.Where(source => !bindings.Any(binding =>
            string.Equals(binding.Key, source, StringComparison.Ordinal))).ToArray();
        string[] inventoryMismatches = inventoryMismatchesFor(bindings, documented, checks);
        string[] missingCheckIds = bindings
            .Where(binding => !checks.Any(check => string.Equals(check.Id, binding.Value, StringComparison.Ordinal)))
            .Select(static binding => binding.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        bool passed =
            headingIndex >= 0 &&
            notObserved.Length == 0 &&
            notInRegistry.Length == 0 &&
            missingFromInventory.Length == 0 &&
            undocumentedInInventory.Length == 0 &&
            uncoveredChecks.Length == 0 &&
            inventoryMismatches.Length == 0 &&
            missingCheckIds.Length == 0;

        yield return Check.Assert(
            "D06.discovery-paths.inventory",
            "Metadata discovery inventory",
            "the implemented discovery sources, the path inventory, and the executed per-path checks are compared",
            "implemented=documented=covered",
            passed ? "implemented=documented=covered" : $"documented={documented.Count}, covered={bindings.Count}",
            $"implemented=[{string.Join(", ", implemented)}]; observed=[{string.Join(", ", observed.OrderBy(static source => source, StringComparer.Ordinal))}]; " +
            $"notCoveredByAnyCheck=[{string.Join(", ", notObserved)}]; " +
            $"notInRegistry=[{string.Join(", ", notInRegistry)}]; " +
            $"notDocumented=[{string.Join(", ", missingFromInventory)}]; " +
            $"documentedWithoutImplementation=[{string.Join(", ", undocumentedInInventory)}]; " +
            $"checksWithoutBinding=[{string.Join(", ", uncoveredChecks)}]; " +
            $"rowMismatches=[{string.Join(", ", inventoryMismatches)}]; " +
            $"missingCheckIds=[{string.Join(", ", missingCheckIds)}]",
            passed);
    }

    /// <summary>
    /// Compares the executed bindings of every discovery source with the inventory row that documents it: the
    /// inventory must name a check that actually exercises the source, so documenting a path with the wrong
    /// check id fails the gate.
    /// </summary>
    private static string[] inventoryMismatchesFor(
        IReadOnlyList<KeyValuePair<string, string>> bindings,
        IReadOnlyList<KeyValuePair<string, string>> documented,
        IReadOnlyList<CheckOutcome> checks)
    {
        var mismatches = new List<string>();

        foreach (KeyValuePair<string, string> row in documented)
        {
            string[] exercised = bindings
                .Where(binding => string.Equals(binding.Key, row.Key, StringComparison.Ordinal))
                .Select(static binding => binding.Value)
                .ToArray();

            if (exercised.Length == 0)
            {
                continue;
            }

            if (!exercised.Contains(row.Value, StringComparer.Ordinal) ||
                !checks.Any(check => string.Equals(check.Id, row.Value, StringComparison.Ordinal)))
            {
                mismatches.Add($"{row.Key}: inventory={row.Value}, exercised=[{string.Join(", ", exercised)}]");
            }
        }

        return mismatches.OrderBy(static mismatch => mismatch, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Locates the inventory document from the running output directory, without depending on the working
    /// directory the process was started from.
    /// </summary>
    private static string? LocateDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "docs", "compatibility-rules.md");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
