using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Writes the canonical contract document for the representative root contract, so the same bytes can be
/// compared across separate runs.
/// </summary>
internal static class CanonicalOutput
{
    public const string RepresentativeRootFileName = "order-envelope.contract.json";

    public static string RenderDocument()
    {
        JsonTypeInfo contract = JsonContractOptions.Reflection().GetTypeInfo(typeof(OrderEnvelope));
        return KeelMatrix.JsonDrift.JsonDrift.Extract(contract).CanonicalJson;
    }

    /// <summary>
    /// Writes the canonical document and returns the exact bytes that were written to disk, so line-ending
    /// and byte order mark properties can be asserted on the artifact rather than on an intermediate value.
    /// </summary>
    public static byte[] WriteDocument(string directory)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, RepresentativeRootFileName);
        File.WriteAllText(path, RenderDocument(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return File.ReadAllBytes(path);
    }

    public static int Write(string directory)
    {
        byte[] bytes = WriteDocument(directory);
        string path = Path.Combine(directory, RepresentativeRootFileName);

        Console.WriteLine($"canonical-document: {path}");
        Console.WriteLine($"bytes: {bytes.Length}");
        Console.WriteLine($"sha256: {Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}");

        return 0;
    }
}
