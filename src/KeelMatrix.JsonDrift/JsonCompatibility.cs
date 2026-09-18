namespace KeelMatrix.JsonDrift;

/// <summary>
/// Defines the compatibility question that JsonDrift can answer for a structural JSON wire contract.
/// </summary>
/// <remarks>
/// Source/API compatibility asks whether C# or public API consumers still compile. Business/semantic
/// compatibility asks whether values retain their domain meaning. JsonDrift does not claim either of those
/// properties. <see cref="ReaderBackward"/> asks only whether documented wire data accepted by the earlier
/// contract remains readable, without loss, under the later contract.
/// </remarks>
public enum JsonCompatibility
{
    /// <summary>
    /// The later contract can read documented wire data from the earlier contract without losing that data.
    /// </summary>
    ReaderBackward,
}
