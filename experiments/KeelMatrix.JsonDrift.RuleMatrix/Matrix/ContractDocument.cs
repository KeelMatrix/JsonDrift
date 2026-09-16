using System.Text.Json.Nodes;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Reads back the fields of a canonical contract document so a check can assert on what the contract model
/// actually recorded instead of on the code that was supposed to write it.
/// </summary>
internal static class ContractDocument
{
    public static JsonObject Parse(string document) =>
        JsonNode.Parse(document)?.AsObject()
        ?? throw new InvalidOperationException("the canonical document could not be parsed");

    public static JsonObject Root(string document) =>
        Parse(document)["root"]?.AsObject()
        ?? throw new InvalidOperationException("the canonical document has no root object");

    public static bool RootSupported(string document) =>
        Root(document)["supported"]?.GetValue<bool>()
        ?? throw new InvalidOperationException("the canonical document root has no supported flag");

    /// <summary>
    /// The aggregate support state: false when any reachable metadata recorded by the document is
    /// unsupported, including nested members, nested shapes, enum wire identities, and derived types.
    /// </summary>
    public static bool OverallSupported(string document) =>
        Parse(document)["overallSupported"]?.GetValue<bool>()
        ?? throw new InvalidOperationException("the canonical document has no overallSupported flag");

    public static JsonObject? Member(string document, string memberName)
    {
        JsonArray members = Root(document)["members"]?.AsArray()
            ?? throw new InvalidOperationException("the canonical document root has no members");

        foreach (JsonNode? member in members)
        {
            if (member is not null &&
                string.Equals(member["name"]?.GetValue<string>(), memberName, StringComparison.Ordinal))
            {
                return member.AsObject();
            }
        }

        return null;
    }

    public static bool MemberSupported(string document, string memberName) =>
        Member(document, memberName)?["supported"]?.GetValue<bool>() == true;

    public static bool MemberHasShape(string document, string memberName) =>
        Member(document, memberName)?.ContainsKey("shape") == true;
}
