namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Reads the developer-facing tables of <c>docs/compatibility-rules.md</c> so the executed checks can bind
/// the code to the documentation: the classification rule table, the path inventory, and the allowlist lines.
/// </summary>
internal static class DocumentationTables
{
    /// <summary>The heading that starts the classification rule table.</summary>
    public const string RuleTableHeading = "### Classification rule table";

    /// <summary>The heading that starts the metadata discovery path inventory.</summary>
    public const string InventoryHeading = "### Metadata discovery path inventory";

    private const string ScalarAllowlistPrefix = "Allowlisted framework scalar types:";
    private const string ConverterAllowlistPrefix = "Allowlisted framework converters:";
    private const string ResolverAllowlistPrefix = "Allowlisted metadata resolvers:";

    /// <summary>Locates the rule document from the running output directory.</summary>
    public static string? Locate()
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

    /// <summary>Reads the document, or returns null when it cannot be located.</summary>
    public static string[]? ReadLines()
    {
        string? path = Locate();
        return path is null ? null : File.ReadAllLines(path);
    }

    /// <summary>
    /// Reads the rows of a Markdown table that starts at <paramref name="heading"/>: every non-separator row
    /// up to the next second-level heading, with the backticks removed from a code cell.
    /// </summary>
    public static IReadOnlyList<string[]> ReadTable(string[] lines, string heading)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(heading);

        var rows = new List<string[]>();
        int headingIndex = Array.FindIndex(lines, line => string.Equals(line.Trim(), heading, StringComparison.Ordinal));

        if (headingIndex < 0)
        {
            return rows;
        }

        for (int index = headingIndex + 1; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            string[]? cells = MetadataDiscoverySources.SplitTableRow(lines[index]);

            if (cells is null || cells.All(static cell => cell.Length == 0 || cell.All(character => character == '-')))
            {
                continue;
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>Reads the ordered, comma-separated names of one allowlist line.</summary>
    public static IReadOnlyList<string>? ReadAllowlist(string[] lines, AllowlistKind kind)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string prefix = Prefix(kind);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed[prefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static name => name.Trim().Trim('`'))
                .Where(static name => name.Length > 0)
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// Parses one row of the classification rule table, of the form
    /// <c>| `rule-id` | Supported | construct |</c>.
    /// </summary>
    public static bool TryParseRuleRow(string[] cells, out string ruleId, out bool supported)
    {
        ArgumentNullException.ThrowIfNull(cells);

        ruleId = string.Empty;
        supported = false;

        if (cells.Length < 2)
        {
            return false;
        }

        string id = cells[0].Trim().Trim('`').Trim();
        string verdict = cells[1].Trim().Trim('`').Trim();

        if (id.Length == 0 || verdict.Length == 0)
        {
            return false;
        }

        if (string.Equals(verdict, "Supported", StringComparison.OrdinalIgnoreCase))
        {
            ruleId = id;
            supported = true;
            return true;
        }

        if (string.Equals(verdict, "Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            ruleId = id;
            supported = false;
            return true;
        }

        return false;
    }

    private static string Prefix(AllowlistKind kind) => kind switch
    {
        AllowlistKind.ScalarTypes => ScalarAllowlistPrefix,
        AllowlistKind.Converters => ConverterAllowlistPrefix,
        AllowlistKind.Resolvers => ResolverAllowlistPrefix,
    };
}

/// <summary>One allowlist whose documented names are compared with the code allowlist.</summary>
internal enum AllowlistKind
{
    ScalarTypes,
    Converters,
    Resolvers,
}
