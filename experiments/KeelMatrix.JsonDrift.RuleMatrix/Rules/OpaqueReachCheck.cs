using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// Builds the check that proves one metadata path cannot report opaque metadata as classifiable: the
/// classifier reports the contract unsupported, the canonical document records it as unsupported in both its
/// root record and its aggregate flag, and a report that contains it can never be green. A check can also
/// require recorded evidence in the document, so a shape that the traversal failed to record fails the check
/// instead of passing because the verdict happened to be unsupported for another reason.
/// </summary>
internal static class OpaqueReachCheck
{
    public const string Expected = "metadata=Unsupported, canonical=Unsupported, overall=Unsupported, report=Unsupported";

    public static CheckOutcome Create(
        string id,
        string change,
        string source,
        string pathId,
        JsonTypeInfo contract,
        string title = "Opaque converter",
        string documentEvidence = "",
        string rejectedEvidence = "")
    {
        ArgumentNullException.ThrowIfNull(contract);

        string? reason = ContractClassifier.DescribeUnsupported(contract);
        string document = ContractCanonicalizer.Canonicalize(contract);
        bool canonicalSupported = ContractDocument.RootSupported(document);
        bool canonicalOverallSupported = ContractDocument.OverallSupported(document);
        bool evidenceRecorded =
            documentEvidence.Length == 0 || document.Contains(documentEvidence, StringComparison.Ordinal);
        bool rejectedEvidenceRecorded =
            rejectedEvidence.Length > 0 && document.Contains(rejectedEvidence, StringComparison.Ordinal);

        JsonDriftReport report = JsonDrift.Compare(contract, JsonDrift.Extract(contract), JsonCompatibility.ReaderBackward);

        bool assertionFailed = false;

        try
        {
            report.AssertCompatible();
        }
        catch (JsonDriftCompatibilityException)
        {
            assertionFailed = true;
        }

        string evidence = documentEvidence.Length == 0
            ? "evidence=not-required"
            : $"evidence={(evidenceRecorded ? "recorded" : "missing")}";
        string rejected = rejectedEvidence.Length == 0
            ? "rejectedEvidence=not-required"
            : $"rejectedEvidence={(rejectedEvidenceRecorded ? "present" : "absent")}";

        return new CheckOutcome(
            id,
            title,
            change,
            Expected,
            $"metadata={(reason is null ? "Supported" : "Unsupported")}, canonical={(canonicalSupported ? "Supported" : "Unsupported")}, " +
            $"overall={(canonicalOverallSupported ? "Supported" : "Unsupported")}, report={report.Outcome}",
            $"source={source}; path={pathId}; reason: {(reason is null ? "none" : MetadataDiscoverySources.Reason(reason))}; " +
            $"{evidence}; {rejected}; assertion failed: {assertionFailed}",
            reason is not null &&
            !canonicalSupported &&
            !canonicalOverallSupported &&
            report.Outcome == JsonDriftClassification.Unsupported &&
            assertionFailed &&
            evidenceRecorded &&
            !rejectedEvidenceRecorded,
            source,
            pathId);
    }
}
