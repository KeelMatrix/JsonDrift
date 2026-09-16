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

    /// <summary>The recorded serializer option values of the canonical document.</summary>
    public static JsonObject Options(string document) =>
        Parse(document)["options"]?.AsObject()
        ?? throw new InvalidOperationException("the canonical document has no recorded option values");

    /// <summary>The recorded value of one serializer option family, or null when it was not recorded.</summary>
    public static string? OptionValue(string document, string optionId) =>
        Options(document)[optionId]?.GetValue<string>();

    public static bool RootSupported(string document) =>
        Root(document)["supported"]?.GetValue<bool>()
        ?? throw new InvalidOperationException("the canonical document root has no supported flag");

    /// <summary>The classification rule the document recorded for the root contract.</summary>
    public static string? RootRule(string document) => Root(document)["rule"]?.GetValue<string>();

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

    /// <summary>The classification rule the document recorded for one member.</summary>
    public static string? MemberRule(string document, string memberName) =>
        Member(document, memberName)?["rule"]?.GetValue<string>();

    /// <summary>The JSON token kind the document recorded for one member.</summary>
    public static string? MemberTokenKind(string document, string memberName) =>
        Member(document, memberName)?["tokenKind"]?.GetValue<string>();

    /// <summary>The recorded wire identity of an enum member, or null when the member records none.</summary>
    public static JsonObject? MemberEnumWire(string document, string memberName) =>
        Member(document, memberName)?["enumWire"] as JsonObject;

    /// <summary>The recorded wire identity of a member of a registered derived type.</summary>
    public static JsonObject? DerivedTypeMemberEnumWire(string document, string memberName)
    {
        JsonArray? derivedTypes = Root(document)["polymorphism"]?["derivedTypes"]?.AsArray();

        if (derivedTypes is null)
        {
            return null;
        }

        foreach (JsonNode? derived in derivedTypes)
        {
            JsonArray? members = derived?["members"]?.AsArray();

            if (members is null)
            {
                continue;
            }

            foreach (JsonNode? member in members)
            {
                if (string.Equals(member?["name"]?.GetValue<string>(), memberName, StringComparison.Ordinal))
                {
                    return member?["enumWire"] as JsonObject;
                }
            }
        }

        return null;
    }

    /// <summary>The recorded token of one enum member inside a recorded wire identity, without quotes.</summary>
    public static string? EnumWireToken(JsonObject? wire, string enumMemberName) =>
        wire?[enumMemberName]?.ToJsonString().Trim('"');
}
