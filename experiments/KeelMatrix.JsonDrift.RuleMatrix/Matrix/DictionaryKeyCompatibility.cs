namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Classifies a dictionary key-type change. Reader compatibility depends on whether every key the earlier
/// contract could write is losslessly representable in the later key type: an earlier
/// <c>Dictionary&lt;string, T&gt;</c> can write a key the later <c>Dictionary&lt;int, T&gt;</c> rejects.
/// The contract model records key type identity but not the earlier key value space, so a key-type change
/// is reported unsupported rather than as a general classification.
/// </summary>
internal static class DictionaryKeyCompatibility
{
    public static string? DescribeUnsupported(Type earlierKeyType, Type laterKeyType)
    {
        ArgumentNullException.ThrowIfNull(earlierKeyType);
        ArgumentNullException.ThrowIfNull(laterKeyType);

        if (earlierKeyType == laterKeyType)
        {
            return null;
        }

        return $"dictionary key type changes from {TypeShapes.TypeName(earlierKeyType)} to "
            + $"{TypeShapes.TypeName(laterKeyType)}; compatibility depends on the earlier key value space, "
            + "which the contract model does not record";
    }
}
