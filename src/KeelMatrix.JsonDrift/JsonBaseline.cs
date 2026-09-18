using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift;

/// <summary>Performs explicit, local canonical baseline creation, replacement, and reading.</summary>
/// <remarks>
/// Reading never creates or rewrites a file. Creation refuses an existing path unless the caller explicitly
/// supplies the overwrite flag, and update always requires that explicit flag. Non-overwriting creation uses
/// exclusive file creation. Updates write a flushed temporary file beside the existing baseline and use an
/// atomic replacement operation that requires the destination to still exist; a concurrent deletion therefore
/// fails instead of turning the update into a create. Baselines are local source-controlled artifacts; this API
/// has no hosted or registry behavior.
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
        JsonBaselineLimits effectiveLimits = limits ?? JsonBaselineLimits.Default;
        byte[] bytes = ReadBounded(path, effectiveLimits);
        return JsonContract.FromCanonicalJson(bytes, effectiveLimits);
    }

    private static void Write(JsonContract contract, string path, bool overwrite, string operation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null && operation != "update")
        {
            Directory.CreateDirectory(directory);
        }

        byte[] bytes = contract.GetCanonicalUtf8();

        if (!overwrite)
        {
            try
            {
                using FileStream stream = new(
                    fullPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            catch (IOException exception)
            {
                throw new IOException(
                    $"Cannot {operation} baseline because '{fullPath}' already exists or cannot be created exclusively; pass overwrite: true only when replacement is intended.",
                    exception);
            }

            return;
        }

        ReplaceAtomically(fullPath, directory, bytes, requireExisting: operation == "update", operation);
    }

    private static JsonContract CreateExtracted(JsonContract contract, string path, bool overwrite)
    {
        Write(contract, path, overwrite, "create");
        return contract;
    }

    private static byte[] ReadBounded(string path, JsonBaselineLimits limits)
    {
        string fullPath = Path.GetFullPath(path);

        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        long length = stream.Length;
        if (length > limits.MaximumBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the maximum size of {limits.MaximumBytes:N0} bytes.");
        }

        byte[] bytes = new byte[(int)length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset != bytes.Length)
        {
            Array.Resize(ref bytes, offset);
        }

        if (stream.ReadByte() >= 0)
        {
            throw new InvalidDataException($"Baseline exceeds the maximum size of {limits.MaximumBytes:N0} bytes.");
        }

        return bytes;
    }

    private static void ReplaceAtomically(
        string fullPath,
        string? directory,
        byte[] bytes,
        bool requireExisting,
        string operation)
    {
        string targetDirectory = directory ?? Directory.GetCurrentDirectory();
        string temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.SequentialScan))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (requireExisting)
            {
                try
                {
                    File.Replace(temporaryPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        $"Cannot atomically update baseline '{fullPath}': the existing baseline may have disappeared or become unavailable; no new baseline was created.",
                        exception);
                }
            }
            else
            {
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // The primary write error is more useful than cleanup failure.
            }
        }
    }
}
