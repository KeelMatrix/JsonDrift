using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R09 ignored and included members, including conditional writing.
/// </summary>
internal static class IgnoreRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions omitsNulls = JsonContractOptions.Reflection();
        omitsNulls.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        JsonTypeInfo included = reflection.GetTypeInfo(typeof(SessionV1));
        JsonTypeInfo ignored = reflection.GetTypeInfo(typeof(SessionV2Ignored));
        JsonTypeInfo notice = reflection.GetTypeInfo(typeof(NoteV1));
        JsonTypeInfo noticeOmittingNulls = omitsNulls.GetTypeInfo(typeof(NoteV1));

        var secret = new SessionV1 { Id = 1, Secret = "s3cr3t" };
        var withoutSecret = new SessionV2Ignored { Id = 1, Secret = "s3cr3t" };

        results.AddRange(Check.Classify(
            "R09.ignore.member-excluded",
            "Ignored and included members",
            "a serialized member becomes ignored",
            WireProbe.Across(secret, included, ignored),
            readerBackwardCompatible: false,
            WireProbe.Across(withoutSecret, ignored, included),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R09.ignore.member-included",
            "Ignored and included members",
            "an ignored member becomes serialized again",
            WireProbe.Across(withoutSecret, ignored, included)));

        results.AddRange(Check.Classify(
            "R09.ignore.condition-when-writing-null",
            "Ignored and included members",
            "null members stop being written",
            WireProbe.Across(new NoteV1 { Id = 1, Note = null }, notice, noticeOmittingNulls),
            readerBackwardCompatible: false,
            WireProbe.Across(new NoteV1 { Id = 1, Note = "n" }, noticeOmittingNulls, notice),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R09.ignore.control",
            "Ignored and included members",
            "the contract is unchanged",
            WireProbe.Across(new NoteV1 { Id = 1, Note = "n" }, notice, notice)));

        return results;
    }
}
