using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift;

/// <summary>Performs explicit, local canonical baseline creation, replacement, and reading.</summary>
/// <remarks>
/// Reading never creates or rewrites a file. Creation refuses an existing path unless the caller explicitly
/// supplies the overwrite flag, and update always requires that explicit flag. Baselines are local
/// source-controlled artifacts; this API has no hosted or registry behavior.
/// </remarks>
public static class JsonBaseline
{
    /// <summary>Creates a canonical baseline at a new or explicitly replaceable path.</summary>
    /// <param name="contract">The effective JSON metadata to record.</param>
    /// <param name="path">The local baseline path.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <returns>The extracted contract that was written.</returns>
    public static JsonContract Create(JsonTypeInfo contract, string path, bool overwrite)
    {
        JsonContract extracted = JsonDrift.Extract(contract);
        Write(extracted, path, overwrite, "create");
        return extracted;
    }

    /// <summary>Creates a canonical baseline for a type resolved from serializer options.</summary>
    /// <typeparam name="T">The root CLR type to resolve.</typeparam>
    /// <param name="options">The actual serializer options used by the application.</param>
    /// <param name="path">The local baseline path.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <returns>The extracted contract that was written.</returns>
    public static JsonContract Create<T>(System.Text.Json.JsonSerializerOptions options, string path, bool overwrite) =>
        CreateExtracted(JsonDrift.Extract<T>(options), path, overwrite);

    /// <summary>Replaces an existing baseline only when the caller explicitly allows overwrite.</summary>
    /// <param name="contract">The effective JSON metadata to record.</param>
    /// <param name="path">The existing local baseline path.</param>
    /// <param name="overwrite">Must be <see langword="true"/> to permit replacement.</param>
    /// <returns>The extracted contract that was written.</returns>
    public static JsonContract Update(JsonTypeInfo contract, string path, bool overwrite)
    {
        if (!overwrite)
        {
            throw new InvalidOperationException("Baseline update requires overwrite: true.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Cannot update a baseline that does not exist.", path);
        }

        JsonContract extracted = JsonDrift.Extract(contract);
        Write(extracted, path, overwrite: true, "update");
        return extracted;
    }

    /// <summary>Reads and validates a canonical baseline without writing or modifying it.</summary>
    /// <param name="path">The local baseline path.</param>
    /// <param name="limits">Optional size and depth bounds.</param>
    /// <returns>The validated baseline model.</returns>
    public static JsonContract Read(string path, JsonBaselineLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        return JsonContract.FromCanonicalJson(bytes, limits ?? JsonBaselineLimits.Default);
    }

    private static void Write(JsonContract contract, string path, bool overwrite, string operation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path) && !overwrite)
        {
            throw new IOException($"Cannot {operation} baseline because '{path}' already exists; pass overwrite: true.");
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, contract.GetCanonicalUtf8());
    }

    private static JsonContract CreateExtracted(JsonContract contract, string path, bool overwrite)
    {
        Write(contract, path, overwrite, "create");
        return contract;
    }
}
