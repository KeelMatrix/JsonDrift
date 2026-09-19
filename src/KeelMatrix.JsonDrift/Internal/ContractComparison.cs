using System.Text.Json;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>Compares validated canonical contracts without resolving serializer metadata or running converters.</summary>
internal static class ContractComparison
{
    private const string AdditiveProperty = "R01.property-add.optional";
    private const string RequiredAdded = "R04.requiredness.optional-to-required";
    private const string PropertyRemoved = "R02.property-removal";
    private const string PropertyRenamed = "R03.serialized-name.json-property-name";
    private const string PropertyNamingPolicy = "R03.serialized-name.naming-policy";
    private const string PropertyRenameWithExtensionData = "R03.serialized-name.mitigated-by-extension-data";
    private const string RequiredRemoved = "R04b.requiredness.required-to-optional";
    private const string ReferenceNullableRemoved = "R05.nullability.reference-nullable-removed";
    private const string ValueNullableRemoved = "R05.nullability.value-nullable-removed";
    private const string ValueNullableAdded = "R05.nullability.value-nullable-added";
    private const string TokenKind = "R06.token-kind.number-to-string";
    private const string NumericWidening = "R06.token-kind.numeric-widening";
    private const string NumericNarrowing = "R06.token-kind.numeric-narrowing";
    private const string Shape = "R07.shape.list-to-dictionary";
    private const string DictionaryKey = "R07.shape.dictionary-key-type";
    private const string EnumRename = "R08.enum.member-rename";
    private const string EnumRepresentation = "R08.enum.number-to-string";
    private const string EnumNaming = "R08.enum.naming-policy";
    private const string IgnoreExcluded = "R09.ignore.condition-when-writing-null";
    private const string IgnoreIncluded = "R09.ignore.member-included";
    private const string Discriminator = "R10.polymorphism.discriminator-value-renamed";
    private const string DerivedAdded = "R10b.polymorphism.derived-type-added";
    private const string ExtensionRemoved = "R11.extension-data.removed";
    private const string ExtensionAdded = "R11b.extension-data.added";
    private const string ContextOptions = "R13.source-generation.context-options";

    public static JsonDriftReport Compare(JsonContract earlier, JsonContract later, JsonCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);

        if (compatibility != JsonCompatibility.ReaderBackward)
        {
            throw new ArgumentOutOfRangeException(nameof(compatibility), compatibility, "Only ReaderBackward is supported.");
        }

        using JsonDocument earlierDocument = Parse(earlier);
        using JsonDocument laterDocument = Parse(later);
        var changes = new ChangeSet();

        bool laterRespectsNullableAnnotations = OptionIsTrue(laterDocument.RootElement, "respectNullableAnnotations");

        CollectUnsupported(earlierDocument.RootElement, "earlier", changes);
        CollectUnsupported(laterDocument.RootElement, "later", changes);
        CompareOptions(earlierDocument.RootElement, laterDocument.RootElement, changes);
        CompareNode(
            earlierDocument.RootElement.GetProperty("root"),
            laterDocument.RootElement.GetProperty("root"),
            "root",
            changes,
            laterRespectsNullableAnnotations);

        IReadOnlyList<JsonDriftChange> ordered = changes.ToArray();
        JsonDriftClassification outcome = ordered.Any(static change => change.Classification == JsonDriftClassification.Unsupported)
            ? JsonDriftClassification.Unsupported
            : ordered.Any(static change => change.Classification == JsonDriftClassification.Incompatible)
                ? JsonDriftClassification.Incompatible
                : JsonDriftClassification.Compatible;

        return new JsonDriftReport(outcome, ordered);
    }

    private static JsonDocument Parse(JsonContract contract) =>
        JsonDocument.Parse(contract.GetCanonicalUtf8());

    private static void CollectUnsupported(JsonElement document, string side, ChangeSet changes)
    {
        bool overallSupported = document.GetProperty("overallSupported").GetBoolean();
        CollectUnsupportedNode(document.GetProperty("root"), side, changes);

        if (!overallSupported && changes.Count == 0)
        {
            changes.Add(
                "root",
                JsonDriftClassification.Unsupported,
                "unsupported.metadata-unavailable",
                $"the {side} contract is not fully supported by the measured metadata rules");
        }
    }

    private static void CollectUnsupportedNode(JsonElement node, string side, ChangeSet changes)
    {
        string path = NodePath(node);
        if (!node.GetProperty("supported").GetBoolean())
        {
            string reason = node.TryGetProperty("reason", out JsonElement reasonElement)
                ? reasonElement.GetString()!
                : $"the {side} contract contains metadata that cannot be classified";
            changes.Add(path, JsonDriftClassification.Unsupported, node.GetProperty("rule").GetString()!, reason);
        }

        if (node.TryGetProperty("members", out JsonElement members))
        {
            foreach (JsonElement member in members.EnumerateArray())
            {
                string memberPath = $"{path}.{member.GetProperty("name").GetString()}";
                if (!member.GetProperty("supported").GetBoolean())
                {
                    string reason = member.TryGetProperty("reason", out JsonElement reasonElement)
                        ? reasonElement.GetString()!
                        : $"the {side} member metadata cannot be classified";
                    changes.Add(memberPath, JsonDriftClassification.Unsupported, member.GetProperty("rule").GetString()!, reason);
                }

                if (member.TryGetProperty("shape", out JsonElement shape))
                {
                    CollectUnsupportedNode(shape, side, changes);
                }
            }
        }

        if (node.TryGetProperty("polymorphism", out JsonElement polymorphism))
        {
            foreach (JsonElement derived in polymorphism.GetProperty("derivedTypes").EnumerateArray())
            {
                CollectUnsupportedNode(derived, side, changes);
            }
        }
    }

    private static void CompareOptions(JsonElement earlier, JsonElement later, ChangeSet changes)
    {
        JsonElement oldOptions = earlier.GetProperty("options");
        JsonElement newOptions = later.GetProperty("options");

        foreach (JsonProperty oldOption in oldOptions.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (!newOptions.TryGetProperty(oldOption.Name, out JsonElement newValue) ||
                string.Equals(oldOption.Value.GetString(), newValue.GetString(), StringComparison.Ordinal))
            {
                continue;
            }

            string oldValue = oldOption.Value.GetString()!;
            string currentValue = newValue.GetString()!;

            if (string.Equals(oldOption.Name, "defaultIgnoreCondition", StringComparison.Ordinal))
            {
                if (string.Equals(oldValue, "Default", StringComparison.Ordinal) &&
                    string.Equals(currentValue, "WhenWritingNull", StringComparison.Ordinal))
                {
                    changes.Add(
                        "options.defaultIgnoreCondition",
                        JsonDriftClassification.Incompatible,
                        IgnoreExcluded,
                        "the later serializer omits null members that the earlier contract could write");
                }
                else if (string.Equals(oldValue, "WhenWritingNull", StringComparison.Ordinal) &&
                         string.Equals(currentValue, "Default", StringComparison.Ordinal))
                {
                    changes.Add(
                        "options.defaultIgnoreCondition",
                        JsonDriftClassification.Compatible,
                        IgnoreIncluded,
                        "the later serializer writes members that the earlier contract omitted; earlier data remains readable");
                }
                else
                {
                    AddUnsupportedOptionChange(oldOption.Name, oldValue, currentValue, changes);
                }
            }
            else if (string.Equals(oldOption.Name, "propertyNamingPolicy", StringComparison.Ordinal))
            {
                changes.Add(
                    $"options.{oldOption.Name}",
                    JsonDriftClassification.Incompatible,
                    PropertyNamingPolicy,
                    $"serializer option '{oldOption.Name}' changed from '{oldValue}' to '{currentValue}', changing the serialized member-name policy");
            }
            else
            {
                changes.Add(
                    $"options.{oldOption.Name}",
                    JsonDriftClassification.Incompatible,
                    ContextOptions,
                    $"serializer option '{oldOption.Name}' changed from '{oldValue}' to '{currentValue}'");
            }
        }
    }

    private static void AddUnsupportedOptionChange(string option, string earlier, string later, ChangeSet changes) =>
        changes.Add(
            $"options.{option}",
            JsonDriftClassification.Unsupported,
            ContextOptions,
            $"serializer option '{option}' changed from '{earlier}' to '{later}', and the measured ReaderBackward rules do not classify that change");

    private static void CompareNode(
        JsonElement earlier,
        JsonElement later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations)
    {
        string earlierKind = earlier.GetProperty("kind").GetString()!;
        string laterKind = later.GetProperty("kind").GetString()!;

        if (!string.Equals(earlierKind, laterKind, StringComparison.Ordinal))
        {
            changes.Add(path, JsonDriftClassification.Incompatible, Shape, $"the JSON shape changes from {earlierKind} to {laterKind}");
            return;
        }

        switch (earlierKind)
        {
            case "object":
                CompareObject(earlier, later, path, changes, laterRespectsNullableAnnotations);
                break;
            case "array":
                CompareTypeField(earlier, later, "elementType", path, changes, Shape);
                break;
            case "dictionary":
                CompareTypeField(earlier, later, "keyType", path, changes, DictionaryKey, unsupported: true);
                CompareTypeField(earlier, later, "valueType", path, changes, TokenKind);
                break;
            case "scalar":
                CompareEnumWire(earlier, later, path, changes);
                break;
        }
    }

    private static void CompareObject(
        JsonElement earlier,
        JsonElement later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations)
    {
        bool oldExtension = earlier.GetProperty("extensionData").GetBoolean();
        bool newExtension = later.GetProperty("extensionData").GetBoolean();
        if (oldExtension != newExtension)
        {
            changes.Add(
                $"{path}.extensionData",
                newExtension ? JsonDriftClassification.Compatible : JsonDriftClassification.Incompatible,
                newExtension ? ExtensionAdded : ExtensionRemoved,
                newExtension
                    ? "the later contract preserves previously unknown members as extension data"
                    : "the later contract no longer preserves members without a matching property");
        }

        if (earlier.TryGetProperty("members", out JsonElement oldMembers) && later.TryGetProperty("members", out JsonElement newMembers))
        {
            CompareMembers(oldMembers, newMembers, path, changes, laterRespectsNullableAnnotations, newExtension);
        }
        else if (earlier.TryGetProperty("memberNames", out JsonElement oldNames) && later.TryGetProperty("memberNames", out JsonElement newNames))
        {
            CompareMemberNames(oldNames, newNames, path, changes);
        }

        bool hasOldPolymorphism = earlier.TryGetProperty("polymorphism", out JsonElement oldPolymorphism);
        bool hasNewPolymorphism = later.TryGetProperty("polymorphism", out JsonElement newPolymorphism);
        if (hasOldPolymorphism || hasNewPolymorphism)
        {
            ComparePolymorphism(
                hasOldPolymorphism ? oldPolymorphism : null,
                hasNewPolymorphism ? newPolymorphism : null,
                path,
                changes,
                laterRespectsNullableAnnotations);
        }
    }

    private static void CompareMembers(
        JsonElement earlier,
        JsonElement later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations,
        bool laterExtensionData)
    {
        var oldByName = earlier.EnumerateArray().ToDictionary(static member => member.GetProperty("name").GetString()!, StringComparer.Ordinal);
        var newByName = later.EnumerateArray().ToDictionary(static member => member.GetProperty("name").GetString()!, StringComparer.Ordinal);
        var renames = FindExplicitRenames(oldByName, newByName);

        foreach (string name in oldByName.Keys.Union(newByName.Keys, StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
        {
            bool oldExists = oldByName.TryGetValue(name, out JsonElement oldMember);
            bool newExists = newByName.TryGetValue(name, out JsonElement newMember);
            string memberPath = $"{path}.{name}";

            if (!oldExists)
            {
                if (renames.Values.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                bool required = newMember.GetProperty("required").GetBoolean();
                changes.Add(
                    memberPath,
                    required ? JsonDriftClassification.Incompatible : JsonDriftClassification.Compatible,
                    required ? RequiredAdded : AdditiveProperty,
                    required
                        ? "the later contract adds a required member that earlier documents do not contain"
                        : "the later contract adds an optional member; earlier members remain readable");
                continue;
            }

            if (!newExists)
            {
                if (renames.TryGetValue(name, out string? newName))
                {
                    changes.Add(
                        memberPath,
                        laterExtensionData ? JsonDriftClassification.Compatible : JsonDriftClassification.Incompatible,
                        laterExtensionData ? PropertyRenameWithExtensionData : RenameRule(oldByName[name], newByName[newName]),
                        laterExtensionData
                            ? $"the renamed member '{name}' is preserved as extension data under the later contract"
                            : $"the serialized member name changes from '{name}' to '{newName}'");
                    continue;
                }

                changes.Add(memberPath, JsonDriftClassification.Incompatible, PropertyRemoved, "the later contract no longer preserves a member written by the earlier contract");
                continue;
            }

            CompareMember(oldMember, newMember, memberPath, changes, laterRespectsNullableAnnotations);
        }
    }

    private static Dictionary<string, string> FindExplicitRenames(
        IReadOnlyDictionary<string, JsonElement> earlier,
        IReadOnlyDictionary<string, JsonElement> later)
    {
        var oldOnly = earlier.Keys.Except(later.Keys, StringComparer.Ordinal).ToArray();
        var newOnly = later.Keys.Except(earlier.Keys, StringComparer.Ordinal).ToArray();
        var matches = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string oldName in oldOnly)
        {
            string[] candidates = newOnly
                .Where(newName => HasExplicitPropertyName(earlier[oldName]) &&
                    HasExplicitPropertyName(later[newName]) &&
                    SameMemberShape(earlier[oldName], later[newName]))
                .ToArray();

            if (candidates.Length == 1)
            {
                matches[oldName] = candidates[0];
            }
        }

        return matches.Count == 1 ? matches : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static bool SameMemberShape(JsonElement earlier, JsonElement later) =>
        string.Equals(earlier.GetProperty("declaredType").GetString(), later.GetProperty("declaredType").GetString(), StringComparison.Ordinal) &&
        string.Equals(earlier.GetProperty("tokenKind").GetString(), later.GetProperty("tokenKind").GetString(), StringComparison.Ordinal) &&
        earlier.GetProperty("required").GetBoolean() == later.GetProperty("required").GetBoolean() &&
        earlier.GetProperty("getNullable").GetBoolean() == later.GetProperty("getNullable").GetBoolean() &&
        earlier.GetProperty("setNullable").GetBoolean() == later.GetProperty("setNullable").GetBoolean() &&
        earlier.GetProperty("extensionData").GetBoolean() == later.GetProperty("extensionData").GetBoolean();

    private static bool HasExplicitPropertyName(JsonElement member) =>
        member.GetProperty("declaredAttributes").EnumerateArray().Any(static attribute =>
            string.Equals(attribute.GetProperty("type").GetString(), "System.Text.Json.Serialization.JsonPropertyNameAttribute", StringComparison.Ordinal));

    private static string RenameRule(JsonElement earlier, JsonElement later) =>
        HasExplicitPropertyName(earlier) || HasExplicitPropertyName(later) ? PropertyRenamed : PropertyNamingPolicy;

    private static void CompareMemberNames(JsonElement earlier, JsonElement later, string path, ChangeSet changes)
    {
        var oldNames = earlier.EnumerateArray().Select(static name => name.GetString()!).ToHashSet(StringComparer.Ordinal);
        var newNames = later.EnumerateArray().Select(static name => name.GetString()!).ToHashSet(StringComparer.Ordinal);

        foreach (string name in oldNames.Except(newNames, StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
        {
            changes.Add($"{path}.{name}", JsonDriftClassification.Incompatible, PropertyRemoved, "the later nested object no longer contains a member written by the earlier contract");
        }

        foreach (string name in newNames.Except(oldNames, StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
        {
            changes.Add($"{path}.{name}", JsonDriftClassification.Compatible, AdditiveProperty, "the later nested object adds an optional member");
        }
    }

    private static void CompareMember(
        JsonElement earlier,
        JsonElement later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations)
    {
        bool oldRequired = earlier.GetProperty("required").GetBoolean();
        bool newRequired = later.GetProperty("required").GetBoolean();
        if (oldRequired != newRequired)
        {
            changes.Add(
                path,
                newRequired ? JsonDriftClassification.Incompatible : JsonDriftClassification.Compatible,
                newRequired ? RequiredAdded : RequiredRemoved,
                newRequired
                    ? "documents written by the earlier contract may omit this member, but the later contract requires it"
                    : "the later contract relaxes requiredness; earlier documents remain readable");
        }

        CompareNullability(earlier, later, path, changes, laterRespectsNullableAnnotations);

        string oldToken = earlier.GetProperty("tokenKind").GetString()!;
        string newToken = later.GetProperty("tokenKind").GetString()!;
        if (!string.Equals(oldToken, newToken, StringComparison.Ordinal))
        {
            changes.Add(path, JsonDriftClassification.Incompatible, TokenRule(oldToken, newToken), $"the JSON token changes from {oldToken} to {newToken}");
        }
        else if (oldToken == "number" &&
                 !string.Equals(earlier.GetProperty("declaredType").GetString(), later.GetProperty("declaredType").GetString(), StringComparison.Ordinal) &&
                 !IsNullablePair(earlier.GetProperty("declaredType").GetString()!, later.GetProperty("declaredType").GetString()!))
        {
            CompareNumericTypes(earlier.GetProperty("declaredType").GetString()!, later.GetProperty("declaredType").GetString()!, path, changes);
        }
        else if (oldToken is "string" or "object" or "array" &&
                 oldToken == "string" &&
                 !string.Equals(earlier.GetProperty("declaredType").GetString(), later.GetProperty("declaredType").GetString(), StringComparison.Ordinal))
        {
            changes.Add(path, JsonDriftClassification.Unsupported, "unsupported.metadata-unavailable", "the member's scalar CLR type changed without a measured wire classification");
        }

        CompareEnumWire(earlier, later, path, changes);
        CompareShapes(earlier, later, path, changes, laterRespectsNullableAnnotations);
    }

    private static void CompareNullability(
        JsonElement earlier,
        JsonElement later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations)
    {
        bool oldNullable = earlier.GetProperty("getNullable").GetBoolean() || earlier.GetProperty("setNullable").GetBoolean();
        bool newNullable = later.GetProperty("getNullable").GetBoolean() || later.GetProperty("setNullable").GetBoolean();
        if (oldNullable == newNullable)
        {
            return;
        }

        bool valueType = IsNullableValueType(earlier.GetProperty("declaredType").GetString()!);
        if (oldNullable && !newNullable)
        {
            bool enforced = laterRespectsNullableAnnotations;
            changes.Add(
                path,
                valueType || enforced ? JsonDriftClassification.Incompatible : JsonDriftClassification.Compatible,
                valueType ? ValueNullableRemoved : ReferenceNullableRemoved,
                valueType
                    ? "the later contract cannot read an explicit null for this value member"
                    : enforced
                        ? "the later contract enforces nullable annotations and rejects an earlier nullable value"
                        : "the later contract still reads null by default; the nullable metadata change is wire-compatible");
        }
        else
        {
            changes.Add(path, JsonDriftClassification.Compatible, ValueNullableAdded, "the later contract accepts null where the earlier contract did not");
        }
    }

    private static void CompareShapes(
        JsonElement earlier,
        JsonElement later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations)
    {
        bool hasOld = earlier.TryGetProperty("shape", out JsonElement oldShape);
        bool hasNew = later.TryGetProperty("shape", out JsonElement newShape);
        if (hasOld != hasNew)
        {
            changes.Add(path, JsonDriftClassification.Unsupported, "unsupported.shape-evidence-missing", "one contract contains shape metadata that the other does not record");
            return;
        }

        if (hasOld)
        {
            CompareNode(oldShape, newShape, path, changes, laterRespectsNullableAnnotations);
        }
    }

    private static void CompareTypeField(
        JsonElement earlier,
        JsonElement later,
        string property,
        string path,
        ChangeSet changes,
        string rule,
        bool unsupported = false)
    {
        bool oldExists = earlier.TryGetProperty(property, out JsonElement oldType);
        bool newExists = later.TryGetProperty(property, out JsonElement newType);
        if (!oldExists || !newExists || string.Equals(oldType.GetString(), newType.GetString(), StringComparison.Ordinal))
        {
            return;
        }

        if (unsupported)
        {
            changes.Add($"{path}.{property}", JsonDriftClassification.Unsupported, rule, "dictionary key type changed, but the contract does not record the key value space");
        }
        else
        {
            changes.Add($"{path}.{property}", JsonDriftClassification.Incompatible, rule, $"the nested type changes from {oldType.GetString()} to {newType.GetString()}");
        }
    }

    private static void ComparePolymorphism(
        JsonElement? earlier,
        JsonElement? later,
        string path,
        ChangeSet changes,
        bool laterRespectsNullableAnnotations)
    {
        if (earlier is null || later is null)
        {
            changes.Add(
                $"{path}.polymorphism",
                later is null ? JsonDriftClassification.Incompatible : JsonDriftClassification.Compatible,
                later is null ? Discriminator : DerivedAdded,
                later is null ? "the later contract no longer records the earlier polymorphic discriminator" : "the later contract adds polymorphic metadata while preserving the earlier contract");
            return;
        }

        JsonElement oldValue = earlier.Value;
        JsonElement newValue = later.Value;
        if (!string.Equals(oldValue.GetProperty("discriminatorPropertyName").GetString(), newValue.GetProperty("discriminatorPropertyName").GetString(), StringComparison.Ordinal))
        {
            changes.Add($"{path}.polymorphism.discriminatorPropertyName", JsonDriftClassification.Incompatible, Discriminator, "the polymorphic discriminator property name changed");
        }

        string oldHandling = oldValue.GetProperty("unknownDerivedTypeHandling").GetString()!;
        string newHandling = newValue.GetProperty("unknownDerivedTypeHandling").GetString()!;
        if (!string.Equals(oldHandling, newHandling, StringComparison.Ordinal))
        {
            changes.Add($"{path}.polymorphism.unknownDerivedTypeHandling", JsonDriftClassification.Unsupported, ContextOptions, "unknown derived-type handling changed without a measured classification");
        }

        var oldDerived = oldValue.GetProperty("derivedTypes").EnumerateArray().ToDictionary(static item => item.GetProperty("discriminator").GetString()!, StringComparer.Ordinal);
        var newDerived = newValue.GetProperty("derivedTypes").EnumerateArray().ToDictionary(static item => item.GetProperty("discriminator").GetString()!, StringComparer.Ordinal);
        foreach (string discriminator in oldDerived.Keys.Union(newDerived.Keys, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal))
        {
            bool oldExists = oldDerived.TryGetValue(discriminator, out JsonElement oldItem);
            bool newExists = newDerived.TryGetValue(discriminator, out JsonElement newItem);
            string itemPath = $"{path}.polymorphism[{discriminator}]";
            if (!oldExists)
            {
                changes.Add(itemPath, JsonDriftClassification.Compatible, DerivedAdded, "the later contract registers an additional derived type");
            }
            else if (!newExists)
            {
                changes.Add(itemPath, JsonDriftClassification.Incompatible, Discriminator, "the later contract no longer recognizes an earlier discriminator");
            }
            else
            {
                CompareNode(oldItem, newItem, itemPath, changes, laterRespectsNullableAnnotations);
            }
        }
    }

    private static void CompareEnumWire(JsonElement earlier, JsonElement later, string path, ChangeSet changes)
    {
        bool oldExists = earlier.TryGetProperty("enumWire", out JsonElement oldWire);
        bool newExists = later.TryGetProperty("enumWire", out JsonElement newWire);
        if (!oldExists || !newExists)
        {
            return;
        }

        var oldMembers = oldWire.EnumerateObject().Where(static property => property.Name is not ("converterType" or "integerTokensAccepted")).ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        var newMembers = newWire.EnumerateObject().Where(static property => property.Name is not ("converterType" or "integerTokensAccepted")).ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        bool oldString = oldMembers.Values.Any(static value => value.ValueKind == JsonValueKind.String);
        bool newString = newMembers.Values.Any(static value => value.ValueKind == JsonValueKind.String);
        if (oldString != newString)
        {
            changes.Add(path, JsonDriftClassification.Incompatible, EnumRepresentation, "the enum wire representation changes between numeric and string tokens");
            return;
        }

        foreach (string member in oldMembers.Keys.Union(newMembers.Keys, StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal))
        {
            bool oldExistsMember = oldMembers.TryGetValue(member, out JsonElement oldValue);
            bool newExistsMember = newMembers.TryGetValue(member, out JsonElement newValue);
            string memberPath = $"{path}.enum[{member}]";
            if (!oldExistsMember)
            {
                continue;
            }

            if (!newExistsMember)
            {
                if (oldString)
                {
                    changes.Add(memberPath, JsonDriftClassification.Incompatible, EnumRename, "the later string enum no longer recognizes an earlier wire name");
                }

                continue;
            }

            if (!string.Equals(oldValue.GetRawText(), newValue.GetRawText(), StringComparison.Ordinal) && oldString)
            {
                changes.Add(memberPath, JsonDriftClassification.Incompatible, EnumNaming, "the serialized string enum name changed");
            }
        }
    }

    private static string TokenRule(string earlier, string later) =>
        earlier == "number" && later == "string" || earlier == "string" && later == "number"
            ? TokenKind
            : Shape;

    private static void CompareNumericTypes(string earlier, string later, string path, ChangeSet changes)
    {
        bool widening = earlier == "System.Int32" && later == "System.Int64";
        bool narrowing = earlier == "System.Int64" && later == "System.Int32";
        changes.Add(
            path,
            narrowing ? JsonDriftClassification.Incompatible : JsonDriftClassification.Compatible,
            narrowing ? NumericNarrowing : widening ? NumericWidening : TokenKind,
            narrowing
                ? $"the later numeric member narrows from {earlier} to {later}"
                : widening
                    ? $"the later numeric member widens from {earlier} to {later}"
                    : $"the numeric member changes from {earlier} to {later}");
    }

    private static bool IsNullableValueType(string typeName) => typeName.StartsWith("System.Nullable<", StringComparison.Ordinal);

    private static bool IsNullablePair(string earlier, string later) =>
        IsNullableValueType(earlier) && string.Equals(earlier["System.Nullable<".Length..^1], later, StringComparison.Ordinal) ||
        IsNullableValueType(later) && string.Equals(earlier, later["System.Nullable<".Length..^1], StringComparison.Ordinal);

    private static bool OptionIsTrue(JsonElement document, string optionName) =>
        document.GetProperty("options").TryGetProperty(optionName, out JsonElement option) &&
        string.Equals(option.GetString(), "true", StringComparison.Ordinal);

    private static string NodePath(JsonElement node) => node.GetProperty("path").GetString()!;

    private sealed class ChangeSet
    {
        private readonly List<JsonDriftChange> changes = new();
        private readonly HashSet<string> keys = new(StringComparer.Ordinal);

        public int Count => changes.Count;

        public void Add(string path, JsonDriftClassification classification, string ruleId, string reason)
        {
            string key = $"{path}\u001f{classification}\u001f{ruleId}\u001f{reason}";
            if (keys.Add(key))
            {
                changes.Add(new JsonDriftChange(path, classification, ruleId, reason));
            }
        }

        public JsonDriftChange[] ToArray() => changes
            .OrderBy(static change => change.Path, StringComparer.Ordinal)
            .ThenBy(static change => change.RuleId, StringComparer.Ordinal)
            .ThenBy(static change => change.Classification)
            .ThenBy(static change => change.Reason, StringComparer.Ordinal)
            .ToArray();
    }
}
