namespace KeelMatrix.JsonDrift;

/// <summary>Describes the outcome of a JsonDrift contract comparison.</summary>
public enum JsonDriftClassification
{
    /// <summary>The later contract reads the earlier contract without a classified loss.</summary>
    Compatible,

    /// <summary>The later contract has a documented structural incompatibility with the earlier contract.</summary>
    Incompatible,

    /// <summary>The comparison contains metadata that the measured rule set cannot classify safely.</summary>
    Unsupported,
}
