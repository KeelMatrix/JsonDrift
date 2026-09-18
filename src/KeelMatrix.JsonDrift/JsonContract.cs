using System.Text;
using System.Text.Json;

namespace KeelMatrix.JsonDrift;

/// <summary>
/// An immutable, versioned canonical description of one effective <see cref="System.Text.Json"/> contract.
/// </summary>
/// <remarks>
/// This model describes structural JSON wire compatibility only. It is not a source/API compatibility model,
/// and it cannot prove business or semantic compatibility. A contract can be explicitly unsupported when the
/// serializer metadata contains a feature that JsonDrift has not measured; unsupported contracts must never be
/// treated as compatible.
/// </remarks>
public sealed class JsonContract
{
    private readonly byte[] canonicalBytes;

    internal JsonContract(
        int formatVersion,
        string rootTypeName,
        bool isSupported,
        string? unsupportedReason,
        byte[] canonicalBytes)
    {
        FormatVersion = formatVersion;
        RootTypeName = rootTypeName;
        IsSupported = isSupported;
        UnsupportedReason = unsupportedReason;
        this.canonicalBytes = canonicalBytes;
    }

    /// <summary>Gets the canonical baseline format version of this contract.</summary>
    public int FormatVersion { get; }

    /// <summary>Gets the stable type name of the selected root contract.</summary>
    public string RootTypeName { get; }

    /// <summary>
    /// Gets a value indicating whether every recorded feature in the contract is classified by the shipped
    /// deny-by-default rules.
    /// </summary>
    public bool IsSupported { get; }

    /// <summary>
    /// Gets the diagnostic naming the unsupported declaration or feature, or <see langword="null"/> when the
    /// contract is supported.
    /// </summary>
    public string? UnsupportedReason { get; }

    /// <summary>Gets the canonical UTF-8 JSON document as a string with LF line endings.</summary>
    public string CanonicalJson => Encoding.UTF8.GetString(canonicalBytes);

    /// <summary>
    /// Returns a copy of the canonical UTF-8 JSON bytes. The bytes have no BOM and end in one LF character.
    /// </summary>
    public byte[] GetCanonicalUtf8() => canonicalBytes.ToArray();

    internal static JsonContract FromCanonicalJson(byte[] bytes, JsonBaselineLimits limits)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(limits);

        if (bytes.Length > limits.MaximumBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the maximum size of {limits.MaximumBytes:N0} bytes.");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new InvalidDataException("Baseline must be UTF-8 without a byte order mark.");
        }

        if (bytes.Contains((byte)'\r'))
        {
            throw new InvalidDataException("Baseline must use LF line endings.");
        }

        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            throw new InvalidDataException("Baseline must end with an LF line ending.");
        }

        using JsonDocument document = ParseJson(bytes, limits.MaximumDepth);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: the root must be an object.");
        }

        if (!root.TryGetProperty("formatVersion", out JsonElement versionElement))
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: missing formatVersion.");
        }

        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
        {
            throw new InvalidDataException("Baseline formatVersion must be an integer.");
        }

        if (version > Internal.ContractCanonicalizer.FormatVersion)
        {
            throw new InvalidDataException($"Baseline format version {version} is newer than supported version {Internal.ContractCanonicalizer.FormatVersion}.");
        }

        if (version != Internal.ContractCanonicalizer.FormatVersion)
        {
            throw new InvalidDataException($"Baseline format version {version} is not supported.");
        }

        if (!root.TryGetProperty("root", out JsonElement contractRoot) || contractRoot.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: missing root contract.");
        }

        if (!contractRoot.TryGetProperty("typeName", out JsonElement typeName) || typeName.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: root typeName is missing.");
        }

        if (!root.TryGetProperty("overallSupported", out JsonElement supported) ||
            (supported.ValueKind != JsonValueKind.True && supported.ValueKind != JsonValueKind.False))
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: overallSupported is missing.");
        }

        string? reason = null;
        if (contractRoot.TryGetProperty("reason", out JsonElement reasonElement) && reasonElement.ValueKind == JsonValueKind.String)
        {
            reason = reasonElement.GetString();
        }

        return new JsonContract(
            version,
            typeName.GetString() ?? string.Empty,
            supported.GetBoolean(),
            reason,
            bytes.ToArray());
    }

    private static JsonDocument ParseJson(byte[] bytes, int maximumDepth)
    {
        try
        {
            return JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    MaxDepth = maximumDepth,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
        }
        catch (JsonException exception) when (exception.Message.Contains("depth", StringComparison.OrdinalIgnoreCase) ||
                                               exception.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Baseline exceeds the maximum nesting depth of {maximumDepth}.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Baseline is malformed JSON.", exception);
        }
    }
}
