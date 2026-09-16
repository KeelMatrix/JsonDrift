using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R08 enum representation: numeric tokens, string tokens, member identity, and the serialized name a
/// string enum member actually uses.
/// </summary>
internal static class EnumRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions numeric = JsonContractOptions.Reflection();
        JsonSerializerOptions text = JsonContractOptions.Reflection(new JsonStringEnumConverter());
        JsonSerializerOptions textOnly = JsonContractOptions.Reflection(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        JsonSerializerOptions camelCase = JsonContractOptions.Reflection(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        JsonSerializerOptions snakeCase = JsonContractOptions.Reflection(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        JsonTypeInfo numericState = numeric.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo renamedState = text.GetTypeInfo(typeof(StateHolderRenamed));
        JsonTypeInfo textState = text.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo textOnlyState = textOnly.GetTypeInfo(typeof(StateHolderNumeric));
        JsonTypeInfo extendedState = numeric.GetTypeInfo(typeof(StateHolderExtended));
        JsonTypeInfo camelStage = camelCase.GetTypeInfo(typeof(StageHolder));
        JsonTypeInfo snakeStage = snakeCase.GetTypeInfo(typeof(StageHolder));
        JsonTypeInfo memberLevelState = text.GetTypeInfo(typeof(StateHolderMemberLevel));

        var numericValue = new StateHolderNumeric { State = OrderState.Shipped };
        var textValue = new StateHolderNumeric { State = OrderState.Shipped };
        var staged = new StageHolder { Stage = OrderStage.InProgress };

        results.AddRange(Check.Classify(
            "R08.enum.member-rename",
            "Enum representation change",
            "an enum member is renamed while the contract keeps string tokens",
            WireProbe.Across(textValue, textState, renamedState),
            readerBackwardCompatible: false,
            WireProbe.Across(new StateHolderRenamed { State = OrderStateRenamed.Dispatched }, renamedState, textState),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R08.enum.naming-policy",
            "Enum representation change",
            "the naming policy applied to string enum members changes",
            WireProbe.Across(staged, camelStage, snakeStage),
            readerBackwardCompatible: false,
            WireProbe.Across(staged, snakeStage, camelStage),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R08.enum.number-to-string",
            "Enum representation change",
            "an enum member is written as a JSON string instead of a number",
            WireProbe.Across(numericValue, numericState, textState),
            readerBackwardCompatible: false,
            WireProbe.Across(textValue, textState, numericState),
            writerForwardCompatible: false));

        results.Add(Check.Incompatible(
            "R08.enum.number-to-string.tokens-only",
            "Enum representation change",
            "string tokens are required and integer tokens are rejected",
            WireProbe.Across(numericValue, numericState, textOnlyState)));

        results.AddRange(Check.Classify(
            "R08.enum.member-insertion",
            "Enum representation change",
            "an enum member is inserted into a numeric representation",
            WireProbe.Across(numericValue, numericState, extendedState),
            readerBackwardCompatible: true,
            WireProbe.Across(new StateHolderExtended { State = OrderStateExtended.Packed }, extendedState, numericState),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R08.enum.control",
            "Enum representation change",
            "the contract is unchanged",
            WireProbe.Across(numericValue, numericState, numericState)));

        AddWireIdentityChecks(results, numericState, textState, renamedState, camelStage, snakeStage);
        AddMemberLevelWireIdentityCheck(results, memberLevelState);

        return results;
    }

    /// <summary>
    /// A member-level converter declaration is the member's effective converter, so the recorded wire name
    /// must come from it rather than from the contract's options or from the enum type. Two contracts whose
    /// wire is identical therefore produce the same recorded identity, and a member that writes strings
    /// inside otherwise numeric options is recorded as strings.
    /// </summary>
    private static void AddMemberLevelWireIdentityCheck(List<CheckOutcome> results, JsonTypeInfo memberLevelState)
    {
        JsonSerializerOptions numeric = JsonContractOptions.Reflection();
        string memberDocument = ContractCanonicalizer.Canonicalize(memberLevelState);
        string numericDocument = ContractCanonicalizer.Canonicalize(numeric.GetTypeInfo(typeof(StateHolderNumeric)));
        string typeLevelDocument = ContractCanonicalizer.Canonicalize(
            JsonContractOptions.Reflection().GetTypeInfo(typeof(PriorityHolder)));


        bool memberRecordsName =
            memberDocument.Contains("\"Shipped\": \"Shipped\"", StringComparison.Ordinal) &&
            !memberDocument.Contains("\"Shipped\": 1", StringComparison.Ordinal);

        bool recordsTheWireTheMemberActuallyUses =
            memberRecordsName &&
            typeLevelDocument.Contains("\"High\": \"High\"", StringComparison.Ordinal) &&
            numericDocument.Contains("\"Shipped\": 1", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R08.enum.member-level.wire-identity",
            "Enum representation change",
            "an enum member declares a framework string-enum converter while the contract options write numbers",
            "canonical document records the member's effective wire name and matches the type-level path for the same wire",
            recordsTheWireTheMemberActuallyUses
                ? "canonical=MemberEffectiveConverterRecorded"
                : "canonical=MemberEffectiveConverterIgnored",
            $"memberLevelRecordsName={memberRecordsName}; numericPathRecordsValue={numericDocument.Contains("\"Shipped\": 1", StringComparison.Ordinal)}; " +
            $"typeLevelRecordsName={typeLevelDocument.Contains("\"High\": \"High\"", StringComparison.Ordinal)}",
            recordsTheWireTheMemberActuallyUses));
    }

    /// <summary>
    /// The behavioral probes above only matter when the contract model can observe the wire identity an enum
    /// member is written with. These checks assert on the canonical document itself, so a change that the
    /// matrix classifies as incompatible can never produce an identical document.
    /// </summary>
    private static void AddWireIdentityChecks(
        List<CheckOutcome> results,
        JsonTypeInfo numericState,
        JsonTypeInfo textState,
        JsonTypeInfo renamedState,
        JsonTypeInfo camelStage,
        JsonTypeInfo snakeStage)
    {
        string textDocument = ContractCanonicalizer.Canonicalize(textState);
        string numericDocument = ContractCanonicalizer.Canonicalize(numericState);
        string renamedDocument = ContractCanonicalizer.Canonicalize(renamedState);
        string camelDocument = ContractCanonicalizer.Canonicalize(camelStage);
        string snakeDocument = ContractCanonicalizer.Canonicalize(snakeStage);

        bool textRecordsIdentity =
            textDocument.Contains("\"Created\": \"Created\"", StringComparison.Ordinal) &&
            textDocument.Contains("\"Shipped\": \"Shipped\"", StringComparison.Ordinal) &&
            textDocument.Contains("\"Cancelled\": \"Cancelled\"", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R08.enum.string-tokens.wire-identity",
            "Enum representation change",
            "an enum member is written as a string token",
            "canonical document records the serialized name of every enum member",
            textRecordsIdentity ? "canonical=MemberWireNamesRecorded" : "canonical=MemberWireNamesMissing",
            $"wireNames=Created,Shipped,Cancelled; recorded={textRecordsIdentity}",
            textRecordsIdentity));

        bool numericRecordsIdentity =
            numericDocument.Contains("\"Created\": 0", StringComparison.Ordinal) &&
            numericDocument.Contains("\"Shipped\": 1", StringComparison.Ordinal) &&
            numericDocument.Contains("\"Cancelled\": 2", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R08.enum.numeric.wire-identity",
            "Enum representation change",
            "an enum member is written as a numeric token",
            "canonical document records the numeric value of every enum member",
            numericRecordsIdentity ? "canonical=MemberWireValuesRecorded" : "canonical=MemberWireValuesMissing",
            $"wireValues=Created:0,Shipped:1,Cancelled:2; recorded={numericRecordsIdentity}",
            numericRecordsIdentity));

        bool policyRecorded =
            !string.Equals(camelDocument, snakeDocument, StringComparison.Ordinal) &&
            camelDocument.Contains("\"InProgress\": \"inProgress\"", StringComparison.Ordinal) &&
            snakeDocument.Contains("\"InProgress\": \"in_progress\"", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R08.enum.naming-policy.document",
            "Enum representation change",
            "the naming policy applied to string enum members changes",
            "canonical documents differ and record the serialized name the applied policy produces",
            $"identical={string.Equals(camelDocument, snakeDocument, StringComparison.Ordinal)}; camelCase={Describe(camelDocument, "inProgress")}; snake_case={Describe(snakeDocument, "in_progress")}",
            $"camelCaseWireNames={camelDocument.Contains("\"inProgress\"", StringComparison.Ordinal)}; snakeCaseWireNames={snakeDocument.Contains("\"in_progress\"", StringComparison.Ordinal)}",
            policyRecorded));

        bool renameRecorded =
            !string.Equals(textDocument, renamedDocument, StringComparison.Ordinal) &&
            textDocument.Contains("\"Shipped\": \"Shipped\"", StringComparison.Ordinal) &&
            renamedDocument.Contains("\"Dispatched\": \"Dispatched\"", StringComparison.Ordinal);

        results.Add(Check.Assert(
            "R08.enum.member-rename.document",
            "Enum representation change",
            "an enum member is renamed in place while the contract keeps string tokens",
            "canonical documents differ and record the renamed member's serialized name",
            $"identical={string.Equals(textDocument, renamedDocument, StringComparison.Ordinal)}",
            $"earlierRecordsShipped={textDocument.Contains("\"Shipped\"", StringComparison.Ordinal)}; laterRecordsDispatched={renamedDocument.Contains("\"Dispatched\"", StringComparison.Ordinal)}",
            renameRecorded));
    }

    private static string Describe(string document, string wireName) =>
        document.Contains(wireName, StringComparison.Ordinal) ? $"contains({wireName})" : $"missing({wireName})";
}
