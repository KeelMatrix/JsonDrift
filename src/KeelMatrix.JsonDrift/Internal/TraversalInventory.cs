namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// The runtime inventory the traversal and the classifier emit while they run: which discovery sources were
/// visited, which recorded node kinds were produced, and which classification rules were applied. The
/// coverage checks read this inventory instead of a hand-maintained list, so a metadata path the walk never
/// visits is visible as a missing entry rather than as a green result.
/// </summary>
internal sealed class TraversalInventory
{
    /// <summary>The inventory of the current process, filled by every traversal and classification.</summary>
    public static TraversalInventory Ledger { get; } = new();

    private readonly object gate = new();
    private readonly HashSet<MetadataSourceKind> sources = new();
    private readonly HashSet<RecordedNodeKind> nodeKinds = new();
    private readonly HashSet<string> rules = new(StringComparer.Ordinal);
    private readonly HashSet<RecordedOptionValue> acceptedOptionValues = new();
    private readonly HashSet<string> acceptedAttributes = new(StringComparer.Ordinal);
    private readonly HashSet<RecordedConverterConfiguration> acceptedConverterConfigurations = new();

    public void RecordSource(MetadataSourceKind source)
    {
        lock (gate)
        {
            sources.Add(source);
        }
    }

    public void RecordNodeKind(RecordedNodeKind kind)
    {
        lock (gate)
        {
            nodeKinds.Add(kind);
        }
    }

    public void RecordRule(string ruleId)
    {
        lock (gate)
        {
            rules.Add(ruleId);
        }
    }

    /// <summary>
    /// Records one serializer option value the classifier accepted, so the option allowlist can be compared
    /// with the values the executed checks were actually measured under. A value that is reported through
    /// <c>unsupported.option-unlisted</c> is never accepted and therefore never recorded here.
    /// </summary>
    public void RecordAcceptedOptionValue(RecordedOptionValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            acceptedOptionValues.Add(value);
        }
    }

    /// <summary>Records one declared attribute the classifier accepted.</summary>
    public void RecordAcceptedAttribute(RecordedAttributeFact value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            acceptedAttributes.Add(value.Display);
        }
    }

    /// <summary>Records one converter configuration the classifier accepted.</summary>
    public void RecordAcceptedConverterConfiguration(RecordedConverterConfiguration value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (gate)
        {
            acceptedConverterConfigurations.Add(value);
        }
    }

    /// <summary>The discovery sources the traversal actually visited.</summary>
    public IReadOnlyList<MetadataSourceKind> VisitedSources()
    {
        lock (gate)
        {
            return sources.OrderBy(static source => source).ToArray();
        }
    }

    /// <summary>The recorded node kinds the traversal actually produced.</summary>
    public IReadOnlyList<RecordedNodeKind> VisitedNodeKinds()
    {
        lock (gate)
        {
            return nodeKinds.OrderBy(static kind => kind).ToArray();
        }
    }

    /// <summary>The classification rules the classifier actually applied.</summary>
    public IReadOnlyList<string> AppliedRules()
    {
        lock (gate)
        {
            return rules.OrderBy(static rule => rule, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>The serializer option values the executed checks were accepted under.</summary>
    public IReadOnlyList<RecordedOptionValue> AcceptedOptionValues()
    {
        lock (gate)
        {
            return acceptedOptionValues
                .OrderBy(static value => value.Kind)
                .ThenBy(static value => value.Value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>The declared attribute facts the executed checks were accepted under.</summary>
    public IReadOnlyList<string> AcceptedAttributes()
    {
        lock (gate)
        {
            return acceptedAttributes.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>The converter configurations the executed checks were accepted under.</summary>
    public IReadOnlyList<RecordedConverterConfiguration> AcceptedConverterConfigurations()
    {
        lock (gate)
        {
            return acceptedConverterConfigurations
                .OrderBy(static value => value.ConverterType, StringComparer.Ordinal)
                .ThenBy(static value => value.IntegerTokensAccepted)
                .ToArray();
        }
    }
}
