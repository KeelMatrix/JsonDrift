namespace KeelMatrix.JsonDrift;

/// <summary>Bounds the size and nesting depth accepted by canonical baseline parsing.</summary>
public sealed class JsonBaselineLimits
{
    /// <summary>The default maximum baseline size in bytes.</summary>
    public const int DefaultMaximumBytes = 1_048_576;

    /// <summary>The default maximum JSON nesting depth.</summary>
    public const int DefaultMaximumDepth = 64;

    /// <summary>Creates limits with the supplied positive byte and depth bounds.</summary>
    /// <param name="maximumBytes">The largest accepted UTF-8 baseline in bytes.</param>
    /// <param name="maximumDepth">The largest accepted JSON nesting depth.</param>
    public JsonBaselineLimits(
        int maximumBytes = DefaultMaximumBytes,
        int maximumDepth = DefaultMaximumDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);

        MaximumBytes = maximumBytes;
        MaximumDepth = maximumDepth;
    }

    /// <summary>Gets the largest accepted baseline size in bytes.</summary>
    public int MaximumBytes { get; }

    /// <summary>Gets the largest accepted JSON nesting depth.</summary>
    public int MaximumDepth { get; }

    internal static JsonBaselineLimits Default { get; } = new();
}
