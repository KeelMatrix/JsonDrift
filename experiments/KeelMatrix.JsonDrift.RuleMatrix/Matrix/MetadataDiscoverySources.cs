using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// The registry of every metadata-resolution path the classifier walks, together with the contract check
/// that proves a contract reached through it cannot be reported as supported when the metadata is opaque.
/// The recursive walk and the executed checks are both compared against this list, so a discovery source
/// that is added without a covering check fails the matrix instead of passing by default.
/// </summary>
internal static class MetadataDiscoverySources
{
    /// <summary>Converter attribute declared on the root contract type, or on a reachable type.</summary>
    public const string TypeConverterAttribute = "type-converter-attribute";

    /// <summary>Converter attribute declared on the member that reaches the type.</summary>
    public const string MemberConverterAttribute = "member-converter-attribute";

    /// <summary>Converter the metadata provider assigned to a member without a converter attribute.</summary>
    public const string MemberCustomConverter = "member-custom-converter";

    /// <summary>Converter registered in <c>JsonSerializerOptions.Converters</c> that can convert a visited type.</summary>
    public const string OptionsConverters = "options-converters";

    /// <summary>Element type of an array or enumerable contract.</summary>
    public const string EnumerableElementTypes = "enumerable-element-types";

    /// <summary>Key type of a dictionary contract.</summary>
    public const string DictionaryKeyTypes = "dictionary-key-types";

    /// <summary>Value type of a dictionary contract.</summary>
    public const string DictionaryValueTypes = "dictionary-value-types";

    /// <summary>Members of an object contract, including the members that were captured from the resolver.</summary>
    public const string ObjectMembers = "object-members";

    /// <summary>Value type captured by a <c>JsonExtensionData</c> member.</summary>
    public const string ExtensionData = "extension-data";

    /// <summary>Parameter type of a binding constructor.</summary>
    public const string ConstructorParameters = "constructor-parameters";

    /// <summary>Derived types registered through <c>JsonPolymorphismOptions.DerivedTypes</c>.</summary>
    public const string PolymorphismDerivedTypes = "polymorphism-derived-types";

    /// <summary>Identity of the metadata resolver that produced the contract.</summary>
    public const string ResolverChain = "resolver-chain";

    private static readonly string[] SourceOrder =
    {
        TypeConverterAttribute,
        MemberConverterAttribute,
        MemberCustomConverter,
        OptionsConverters,
        EnumerableElementTypes,
        DictionaryKeyTypes,
        DictionaryValueTypes,
        ObjectMembers,
        ExtensionData,
        ConstructorParameters,
        PolymorphismDerivedTypes,
        ResolverChain,
    };

    /// <summary>
    /// The discovery paths implemented by the bounded walk, in the order they are documented. The coverage
    /// check compares this list with the inventory in <c>docs/compatibility-rules.md</c> and with the
    /// executed per-path checks.
    /// </summary>
    public static IReadOnlyList<string> Implemented { get; } = new ReadOnlyCollection<string>(SourceOrder);

    /// <summary>
    /// Whether a converter type ships in the framework <c>System.Text.Json</c> assembly. A namespace prefix
    /// is not evidence of framework provenance, because application converters may declare a
    /// <c>System.Text.Json</c> namespace of their own.
    /// </summary>
    public static bool IsKnownConverterType(Type converterType) =>
        converterType.Assembly == typeof(System.Text.Json.JsonSerializer).Assembly;

    /// <summary>
    /// Runs one discovery probe and records which source produced an unsupported reason, so a check can
    /// prove that the walker behaves as the registry claims.
    /// </summary>
    public static string? FindUnsupported(string source, Func<string?> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        string? reason = probe();

        return reason is null ? null : Witness(source, reason);
    }

    /// <summary>
    /// Tags an unsupported reason with the source that discovered it. The tag is removed by
    /// <see cref="Reason"/> before the reason is reported.
    /// </summary>
    public static string Witness(string source, string reason) => $"[{source}] {reason}";

    /// <summary>
    /// The reported reason without its source tag.
    /// </summary>
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

        string trimmed = line.Trim();

        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
        {
            return false;
        }

        string[] cells = trimmed.Split('|');

        if (cells.Length < 5)
        {
            return false;
        }

        string first = TrimCode(cells[1]);
        string second = TrimCode(cells[2]);

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
    /// The inventory rows carried by the checks: each executed path records the discovery source it
    /// exercised and the inventory check that documents it. A source exercised by several checks is
    /// represented once, by the lowest check identifier, so the comparison against the inventory is a set
    /// comparison.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> InventoryBindings(IEnumerable<CheckOutcome> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        return checks
            .Where(static check => check.SourceId is not null && check.PathId is not null)
            .Select(static check => new KeyValuePair<string, string>(check.SourceId!, check.PathId!))
            .GroupBy(static binding => binding.Key, StringComparer.Ordinal)
            .Select(static group => group.OrderBy(static binding => binding.Value, StringComparer.Ordinal).First())
            .OrderBy(static binding => binding.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string TrimCode(string cell) => cell.Trim().Trim('`').Trim();
}
