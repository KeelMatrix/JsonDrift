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

    public static int Write(string directory)
    {
        Directory.CreateDirectory(directory);

        JsonTypeInfo contract = JsonContractOptions.Reflection().GetTypeInfo(typeof(OrderEnvelope));
        string document = ContractCanonicalizer.Canonicalize(contract);
        string path = Path.Combine(directory, RepresentativeRootFileName);

        File.WriteAllText(path, document, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        byte[] bytes = File.ReadAllBytes(path);
        Console.WriteLine($"canonical-document: {path}");
        Console.WriteLine($"bytes: {bytes.Length}");
        Console.WriteLine($"sha256: {Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}");

        return 0;
    }
}
