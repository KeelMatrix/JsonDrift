using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// D01-D03 canonical document determinism, host independence, and termination on recursive contracts.
/// </summary>
internal static class DeterminismRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();

        string first = ContractCanonicalizer.Canonicalize(JsonContractOptions.Reflection().GetTypeInfo(typeof(OrderEnvelope)));
        string second = ContractCanonicalizer.Canonicalize(JsonContractOptions.Reflection().GetTypeInfo(typeof(OrderEnvelope)));
        string third = ContractCanonicalizer.Canonicalize(JsonContractOptions.Reflection().GetTypeInfo(typeof(OrderEnvelope)));

        results.Add(Check.Assert(
            "D01.canonical-document.repeatable",
            "Canonical document",
            "the canonical document is produced from three independently created option sets",
            "canonical=ByteIdentical",
            first == second && second == third ? "canonical=ByteIdentical" : "canonical=BytesDiffer",
            $"sha256={Sha256(first)}, {Sha256(second)}, {Sha256(third)}; equal={first == second && second == third}",
            first == second && second == third));

        results.Add(Check.Assert(
            "D01.canonical-document.line-endings",
            "Canonical document",
            "the canonical document is produced for a representative root contract",
            "canonical=LF and no byte order mark content",
            !first.Contains('\r') && first.EndsWith('\n') ? "canonical=LF" : "canonical=NotLF",
            $"containsCarriageReturn={first.Contains('\r')}; endsWithLineFeed={first.EndsWith('\n')}; length={first.Length}",
            !first.Contains('\r') && first.EndsWith('\n')));

        string[] hostPaths = { Environment.CurrentDirectory, Path.GetTempPath(), "C:\\", "\\\\" };
        string foundPath = hostPaths.FirstOrDefault(path => !string.IsNullOrEmpty(path) && first.Contains(path, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        results.Add(Check.Assert(
            "D01.canonical-document.host-independent",
            "Canonical document",
            "the canonical document is produced while the process runs from a local working directory",
            "canonical=HostIndependent",
            foundPath.Length == 0 ? "canonical=HostIndependent" : $"canonical=ContainsHostPath({foundPath})",
            $"hostPathInDocument={(foundPath.Length == 0 ? "none" : foundPath)}",
            foundPath.Length == 0));

        string converterFirst = ContractCanonicalizer.Canonicalize(
            JsonContractOptions.Reflection(new MoneyConverter(), new TemperatureConverter()).GetTypeInfo(typeof(SequenceProbe)));
        string converterSecond = ContractCanonicalizer.Canonicalize(
            JsonContractOptions.Reflection(new TemperatureConverter(), new MoneyConverter()).GetTypeInfo(typeof(SequenceProbe)));

        results.Add(Check.Assert(
            "D02.canonical-document.options-equivalent",
            "Canonical document",
            "the converter registration order differs between two otherwise equal option sets",
            "canonical=InstanceIndependent",
            converterFirst == converterSecond ? "canonical=InstanceIndependent" : "canonical=InstanceDependent",
            $"sha256={Sha256(converterFirst)}, {Sha256(converterSecond)}; equal={converterFirst == converterSecond}",
            converterFirst == converterSecond));

        string recursiveFirst = ContractCanonicalizer.Canonicalize(JsonContractOptions.Reflection().GetTypeInfo(typeof(TreeNode)));
        string recursiveSecond = ContractCanonicalizer.Canonicalize(JsonContractOptions.Reflection().GetTypeInfo(typeof(TreeNode)));

        results.Add(Check.Assert(
            "D03.canonical-document.recursive-type",
            "Canonical document",
            "a contract contains a member of its own type",
            "canonical=TerminatesAndRepeatable",
            recursiveFirst == recursiveSecond ? "canonical=TerminatesAndRepeatable" : "canonical=TerminatesButDiffers",
            $"sha256={Sha256(recursiveFirst)}, {Sha256(recursiveSecond)}; equal={recursiveFirst == recursiveSecond}; containsSelfReference={recursiveFirst.Contains("parent", StringComparison.OrdinalIgnoreCase)}",
            recursiveFirst == recursiveSecond && recursiveFirst.Contains("parent", StringComparison.OrdinalIgnoreCase)));

        return results;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
}
