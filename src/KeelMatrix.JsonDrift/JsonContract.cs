using System.Text;
using System.Text.Json;
using KeelMatrix.JsonDrift.Internal;

namespace KeelMatrix.JsonDrift;

/// <summary>
/// An immutable, versioned canonical description of one effective <see cref="System.Text.Json"/> contract.
/// </summary>
/// <remarks>
/// This model describes structural JSON wire compatibility only. It is not a source/API compatibility model,
/// and it cannot prove business or semantic compatibility. A contract can be explicitly unsupported when the
/// serializer metadata contains a feature that JsonDrift has not measured; unsupported contracts must never be
/// treated as compatible. Format version 3 records complete nested object, collection-element, and dictionary
/// key/value metadata, including dictionary construction capability, with bounded references for repeated types,
/// and records the JSON token kind of scalar nodes for fail-closed comparison. ReaderBackward comparison retains
/// nullable value acceptance at every value slot, requires evidenced collection and dictionary materialization,
/// compares enum token domains and member materialization, and rejects malformed reference-only graphs before
/// comparison.
/// </remarks>
public sealed class JsonContract
{
    private static readonly string[] TopLevelRequired = { "formatVersion", "options", "root", "overallSupported" };
    private static readonly string[] DiscriminatorProperty = { "discriminator" };
    private static readonly string[] PolymorphismProperty = { "polymorphism" };
    private static readonly string[] ObjectMembersProperties = { "extensionData", "members" };
    private static readonly string[] ObjectMemberNamesProperties = { "extensionData", "memberNames" };
    private static readonly string[] ElementTypeProperty = { "elementType" };
    private static readonly string[] ElementContractProperty = { "element" };
    private static readonly string[] CollectionSemanticsProperty = { "collectionSemantics" };
    private static readonly string[] DictionaryTypeProperties = { "keyType", "valueType" };
    private static readonly string[] DictionaryContractProperties = { "key", "value" };
    private static readonly string[] DictionaryMaterializationProperty = { "dictionaryMaterialization" };
    private static readonly string[] ScalarTokenKindProperty = { "tokenKind" };
    private static readonly string[] EnumWireProperty = { "enumWire" };
    private static readonly string[] ReferenceProperty = { "reference" };
    private static readonly string[] TokenKinds = { "boolean", "string", "number", "object", "array", "any", "opaque" };
    private static readonly string[] PolymorphismRequired = { "discriminatorPropertyName", "unknownDerivedTypeHandling", "derivedTypes" };
    private static readonly string[] AttributeRequired = { "type", "arguments" };
    private static readonly string[] AttributeArgumentRequired = { "name", "value" };
    private static readonly string[] MemberOptional = { "reason", "enumWire", "shape", "included", "constructorBinding" };
    private static readonly string[] ConstructorBindingRequired = { "name", "position", "hasDefaultValue" };

    private readonly byte[] canonicalBytes;
    private readonly bool usesSourceGeneratedMetadata;

    internal JsonContract(
        int formatVersion,
        string rootTypeName,
        bool isSupported,
        string? unsupportedReason,
        byte[] canonicalBytes,
        bool usesSourceGeneratedMetadata = false)
    {
        FormatVersion = formatVersion;
        RootTypeName = rootTypeName;
        IsSupported = isSupported;
        UnsupportedReason = unsupportedReason;
        this.canonicalBytes = canonicalBytes;
        this.usesSourceGeneratedMetadata = usesSourceGeneratedMetadata;
    }

    /// <summary>Gets the canonical baseline format version of this contract; the current version is 3.</summary>
    public int FormatVersion { get; }

    /// <summary>Gets the stable type name of the selected root contract.</summary>
    public string RootTypeName { get; }

    /// <summary>
    /// Gets a value indicating whether every recorded feature in the contract is classified by the shipped
    /// deny-by-default rules.
    /// </summary>
    public bool IsSupported { get; }

    /// <summary>
    /// Gets the diagnostic naming the unsupported declaration or feature, or <see langword="null"/> when the
    /// contract is supported.
    /// </summary>
    public string? UnsupportedReason { get; }

    /// <summary>Gets the canonical UTF-8 JSON document as a string with LF line endings.</summary>
    public string CanonicalJson => Encoding.UTF8.GetString(canonicalBytes);

    /// <summary>
    /// Returns a copy of the canonical UTF-8 JSON bytes. The bytes have no BOM and end in one LF character.
    /// </summary>
    public byte[] GetCanonicalUtf8() => canonicalBytes.ToArray();

    internal bool UsesSourceGeneratedMetadata => usesSourceGeneratedMetadata;

    internal static JsonContract FromCanonicalJson(
        byte[] bytes,
        JsonBaselineLimits limits,
        bool usesSourceGeneratedMetadata = false)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(limits);

        if (bytes.Length > limits.MaximumBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the maximum size of {limits.MaximumBytes:N0} bytes.");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new InvalidDataException("Baseline must be UTF-8 without a byte order mark.");
        }

        if (bytes.Contains((byte)'\r'))
        {
            throw new InvalidDataException("Baseline must use LF line endings.");
        }

        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            throw new InvalidDataException("Baseline must end with an LF line ending.");
        }

        using JsonDocument document = ParseJson(bytes, limits.MaximumDepth);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: the root must be an object.");
        }

        if (!root.TryGetProperty("formatVersion", out JsonElement versionElement))
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: missing formatVersion.");
        }

        if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out int version))
        {
            throw new InvalidDataException("Baseline formatVersion must be an integer.");
        }

        if (version > Internal.ContractCanonicalizer.FormatVersion)
        {
            throw new InvalidDataException($"Baseline format version {version} is newer than supported version {Internal.ContractCanonicalizer.FormatVersion}.");
        }

        if (version != Internal.ContractCanonicalizer.FormatVersion)
        {
            throw new InvalidDataException($"Baseline format version {version} is not supported.");
        }

        RejectDuplicateProperties(root);
        ValidateObjectProperties(
            root,
            required: TopLevelRequired,
            optional: Array.Empty<string>(),
            "top-level");

        JsonElement options = root.GetProperty("options");
        bool optionsSupported = ValidateOptions(options);

        JsonElement contractRoot = root.GetProperty("root");
        if (contractRoot.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: root contract must be an object.");
        }

        bool rootSupported = ValidateNode(contractRoot, includeMembers: true, "root", discriminatorRequired: false);
        ValidateReferenceGraph(contractRoot);

        JsonElement supported = root.GetProperty("overallSupported");
        if (supported.ValueKind != JsonValueKind.True && supported.ValueKind != JsonValueKind.False)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: overallSupported must be a boolean.");
        }

        bool overallSupported = supported.GetBoolean();
        if (overallSupported != rootSupported || (overallSupported && !optionsSupported))
        {
            throw new InvalidDataException("Baseline has inconsistent overallSupported and root support state.");
        }

        string rootTypeName = contractRoot.GetProperty("typeName").GetString()!;
        string? reason = contractRoot.TryGetProperty("reason", out JsonElement reasonElement)
            ? reasonElement.GetString()
            : null;

        return new JsonContract(
            version,
            rootTypeName,
            overallSupported,
            reason,
            bytes.ToArray(),
            usesSourceGeneratedMetadata);
    }

    private static bool ValidateOptions(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Baseline is not a JsonDrift canonical contract: options must be an object.");
        }

        string[] optionIds = SerializerOptionFacts.All.Select(SerializerOptionFacts.Id).ToArray();
        ValidateObjectProperties(options, optionIds, Array.Empty<string>(), "options");

        bool allSupported = true;
        foreach (ContractOptionKind kind in SerializerOptionFacts.All)
        {
            string id = SerializerOptionFacts.Id(kind);
            JsonElement value = options.GetProperty(id);
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            {
                throw new InvalidDataException($"Baseline options property '{id}' must be a non-empty string.");
            }

            allSupported &= ContractAllowlists.IsAllowlistedOptionValue(kind, value.GetString()!);
        }

        return allSupported;
    }

    private static bool ValidateNode(JsonElement node, bool includeMembers, string context, bool discriminatorRequired)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline {context} must be an object.");
        }

        string[] commonRequired = { "typeName", "kind", "acceptsNull", "reachedBy", "path", "rule", "supported", "declaredAttributes" };
        string[] commonOptional = { "reason" };
        ValidateRequiredProperties(node, commonRequired, context);

        if (discriminatorRequired)
        {
            RequireString(node, "discriminator", context, allowEmpty: true);
        }

        string[] nodeOptional = commonOptional
            .Concat(discriminatorRequired ? DiscriminatorProperty : Array.Empty<string>())
            .ToArray();

        RequireString(node, "typeName", context, allowEmpty: false);
        string kind = RequireString(node, "kind", context, allowEmpty: false);
        string reachedBy = RequireString(node, "reachedBy", context, allowEmpty: false);
        RequireString(node, "path", context, allowEmpty: false);
        string rule = RequireString(node, "rule", context, allowEmpty: false);
        RequireBoolean(node, "acceptsNull", context);

        if (!MetadataSourceRules.All.Select(MetadataSourceRules.Id).Contains(reachedBy, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Baseline {context} has an unknown reachedBy value.");
        }

        bool supported = RequireBoolean(node, "supported", context);
        bool hasReason = node.TryGetProperty("reason", out JsonElement reasonElement);
        if (hasReason && (reasonElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reasonElement.GetString())))
        {
            throw new InvalidDataException($"Baseline {context} reason must be a non-empty string.");
        }

        if (!RuleCatalog.Contains(rule) || RuleCatalog.IsSupportedRule(rule) != supported)
        {
            throw new InvalidDataException($"Baseline {context} has an inconsistent classification rule.");
        }

        if (supported != !hasReason)
        {
            throw new InvalidDataException($"Baseline {context} has inconsistent support and reason state.");
        }

        ValidateAttributes(node.GetProperty("declaredAttributes"), context);
        bool allSupported = supported;

        switch (kind)
        {
            case "object":
                string[] objectOptional = nodeOptional.Concat(PolymorphismProperty).ToArray();
                string[] objectRequired = includeMembers
                    ? commonRequired.Concat(ObjectMembersProperties).ToArray()
                    : commonRequired.Concat(ObjectMemberNamesProperties).ToArray();
                ValidateObjectProperties(node, objectRequired, objectOptional, context);
                RequireBoolean(node, "extensionData", context);
                if (includeMembers)
                {
                    allSupported &= ValidateMembers(node.GetProperty("members"), context);
                }
                else
                {
                    ValidateMemberNames(node.GetProperty("memberNames"), context);
                }

                if (node.TryGetProperty("polymorphism", out JsonElement polymorphism))
                {
                    allSupported &= ValidatePolymorphism(polymorphism, context);
                }

                break;

            case "array":
                ValidateObjectProperties(
                    node,
                    supported
                        ? commonRequired.Concat(CollectionSemanticsProperty).Concat(ElementTypeProperty).Concat(ElementContractProperty).ToArray()
                        : commonRequired.Concat(CollectionSemanticsProperty).ToArray(),
                    supported
                        ? nodeOptional.Concat(ElementContractProperty).ToArray()
                        : nodeOptional.Concat(ElementTypeProperty).Concat(ElementContractProperty).ToArray(),
                    context);
                string collectionSemantics = RequireString(node, "collectionSemantics", context, allowEmpty: false);
                if (collectionSemantics is not ("ordered-with-multiplicity" or "unclassified"))
                {
                    throw new InvalidDataException($"Baseline {context} array has an unknown collectionSemantics value.");
                }
                if (node.TryGetProperty("elementType", out _))
                {
                    RequireString(node, "elementType", context, allowEmpty: false);
                }

                if (node.TryGetProperty("element", out JsonElement element))
                {
                    allSupported &= ValidateNode(element, includeMembers: true, $"{context} element", discriminatorRequired: false);
                }

                break;

            case "dictionary":
                ValidateObjectProperties(
                    node,
                    supported
                        ? commonRequired.Concat(DictionaryMaterializationProperty).Concat(DictionaryTypeProperties).Concat(DictionaryContractProperties).ToArray()
                        : commonRequired.Concat(DictionaryMaterializationProperty).ToArray(),
                    supported
                        ? nodeOptional.Concat(DictionaryContractProperties).ToArray()
                        : nodeOptional.Concat(DictionaryTypeProperties).Concat(DictionaryContractProperties).ToArray(),
                    context);
                string dictionaryMaterialization = RequireString(node, "dictionaryMaterialization", context, allowEmpty: false);
                if (dictionaryMaterialization is not ("constructible" or "unclassified"))
                {
                    throw new InvalidDataException($"Baseline {context} dictionary has an unknown dictionaryMaterialization value.");
                }
                if (node.TryGetProperty("keyType", out _))
                {
                    RequireString(node, "keyType", context, allowEmpty: false);
                }

                if (node.TryGetProperty("valueType", out _))
                {
                    RequireString(node, "valueType", context, allowEmpty: false);
                }

                if (node.TryGetProperty("key", out JsonElement key))
                {
                    allSupported &= ValidateNode(key, includeMembers: true, $"{context} key", discriminatorRequired: false);
                }

                if (node.TryGetProperty("value", out JsonElement value))
                {
                    allSupported &= ValidateNode(value, includeMembers: true, $"{context} value", discriminatorRequired: false);
                }

                break;

            case "scalar":
                ValidateObjectProperties(
                    node,
                    supported ? commonRequired.Concat(ScalarTokenKindProperty).ToArray() : commonRequired,
                    nodeOptional.Concat(EnumWireProperty).Concat(ScalarTokenKindProperty).ToArray(),
                    context);
                if (node.TryGetProperty("tokenKind", out JsonElement scalarTokenKind))
                {
                    string tokenKind = RequireString(node, "tokenKind", context, allowEmpty: false);
                    if (!TokenKinds.Contains(tokenKind, StringComparer.Ordinal))
                    {
                        throw new InvalidDataException($"Baseline {context} scalar has an unknown tokenKind.");
                    }
                }
                if (string.Equals(rule, RuleIds.SupportedEnum, StringComparison.Ordinal))
                {
                    if (!node.TryGetProperty("enumWire", out JsonElement enumWire))
                    {
                        throw new InvalidDataException($"Baseline {context} is missing enumWire.");
                    }

                    ValidateEnumWire(enumWire, context);
                }
                else if (node.TryGetProperty("enumWire", out JsonElement scalarEnumWire))
                {
                    ValidateEnumWire(scalarEnumWire, context);
                }

                break;

            case "reference":
                ValidateObjectProperties(node, commonRequired.Concat(ReferenceProperty).ToArray(), nodeOptional, context);
                RequireString(node, "reference", context, allowEmpty: false);
                break;

            case "unavailable":
                ValidateObjectProperties(node, commonRequired, nodeOptional, context);
                break;

            default:
                throw new InvalidDataException($"Baseline {context} has an unknown node kind.");
        }

        return allSupported;
    }

    private static void ValidateReferenceGraph(JsonElement root)
    {
        var nodes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var references = new List<JsonElement>();
        CollectReferenceGraph(root, nodes, references);

        foreach (JsonElement reference in references)
        {
            string sourcePath = reference.GetProperty("path").GetString()!;
            string targetPath = reference.GetProperty("reference").GetString()!;
            var visited = new HashSet<string>(StringComparer.Ordinal) { sourcePath };

            while (true)
            {
                if (!nodes.TryGetValue(targetPath, out JsonElement target))
                {
                    throw new InvalidDataException(
                        $"Baseline reference '{sourcePath}' targets missing node '{targetPath}'.");
                }

                string targetKind = target.GetProperty("kind").GetString()!;
                if (!string.Equals(targetKind, "reference", StringComparison.Ordinal))
                {
                    break;
                }

                if (!visited.Add(targetPath))
                {
                    throw new InvalidDataException(
                        $"Baseline reference '{sourcePath}' is part of a reference-only cycle.");
                }

                targetPath = target.GetProperty("reference").GetString()!;
            }
        }
    }

    private static void CollectReferenceGraph(
        JsonElement node,
        IDictionary<string, JsonElement> nodes,
        ICollection<JsonElement> references)
    {
        string path = node.GetProperty("path").GetString()!;
        if (!nodes.TryAdd(path, node))
        {
            throw new InvalidDataException($"Baseline contract graph contains ambiguous path '{path}'.");
        }

        if (string.Equals(node.GetProperty("kind").GetString(), "reference", StringComparison.Ordinal))
        {
            references.Add(node);
            return;
        }

        if (node.TryGetProperty("members", out JsonElement members))
        {
            foreach (JsonElement member in members.EnumerateArray())
            {
                if (member.TryGetProperty("shape", out JsonElement shape))
                {
                    CollectReferenceGraph(shape, nodes, references);
                }
            }
        }

        foreach (string childProperty in new[] { "element", "key", "value" })
        {
            if (node.TryGetProperty(childProperty, out JsonElement child))
            {
                CollectReferenceGraph(child, nodes, references);
            }
        }

        if (node.TryGetProperty("polymorphism", out JsonElement polymorphism))
        {
            foreach (JsonElement derived in polymorphism.GetProperty("derivedTypes").EnumerateArray())
            {
                CollectReferenceGraph(derived, nodes, references);
            }
        }
    }

    private static bool ValidateMembers(JsonElement members, string context)
    {
        if (members.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Baseline {context} members must be an array.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        bool allSupported = true;
        foreach (JsonElement member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Baseline {context} member must be an object.");
            }

            string memberName = RequireString(member, "name", context, allowEmpty: false);
            if (!names.Add(memberName))
            {
                throw new InvalidDataException($"Baseline {context} contains duplicate member records.");
            }

            string[] required =
            {
                "name", "declaredType", "tokenKind", "required", "getNullable", "setNullable", "extensionData",
                "canSerialize", "canDeserialize", "declaredAttributes", "rule", "supported",
            };
            ValidateObjectProperties(member, required, MemberOptional, $"{context} member");
            RequireString(member, "declaredType", context, allowEmpty: false);
            string tokenKind = RequireString(member, "tokenKind", context, allowEmpty: false);
            if (!TokenKinds.Contains(tokenKind, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Baseline {context} member has an unknown tokenKind.");
            }

            RequireBoolean(member, "required", context);
            RequireBoolean(member, "getNullable", context);
            RequireBoolean(member, "setNullable", context);
            RequireBoolean(member, "extensionData", context);
            RequireBoolean(member, "canSerialize", context);
            RequireBoolean(member, "canDeserialize", context);
            bool included = true;
            if (member.TryGetProperty("included", out JsonElement includedElement))
            {
                if (includedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException($"Baseline {context} member property 'included' must be a boolean.");
                }

                included = includedElement.GetBoolean();
            }
            bool memberSupported = ValidateClassification(member, $"{context} member");
            ValidateAttributes(member.GetProperty("declaredAttributes"), $"{context} member");

            if (member.TryGetProperty("constructorBinding", out JsonElement constructorBinding))
            {
                ValidateConstructorBinding(constructorBinding, $"{context} member");
            }

            if (memberSupported && tokenKind == "opaque")
            {
                throw new InvalidDataException($"Baseline {context} member has an opaque tokenKind but is supported.");
            }

            if (tokenKind == "opaque" && !member.TryGetProperty("reason", out _))
            {
                throw new InvalidDataException($"Baseline {context} opaque member is missing a reason.");
            }

            if (member.TryGetProperty("enumWire", out JsonElement enumWire))
            {
                ValidateEnumWire(enumWire, $"{context} member");
            }

            if (member.TryGetProperty("shape", out JsonElement shape))
            {
                memberSupported &= ValidateNode(shape, includeMembers: true, $"{context} member shape", discriminatorRequired: false);
            }
            else if (included && memberSupported && (tokenKind == "object" || tokenKind == "array"))
            {
                throw new InvalidDataException($"Baseline {context} supported complex member is missing shape.");
            }

            allSupported &= memberSupported;
        }

        return allSupported;
    }

    private static void ValidateConstructorBinding(JsonElement binding, string context)
    {
        if (binding.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline {context} constructorBinding must be an object.");
        }

        ValidateObjectProperties(binding, ConstructorBindingRequired, Array.Empty<string>(), $"{context} constructorBinding");
        RequireString(binding, "name", context, allowEmpty: true);
        JsonElement position = binding.GetProperty("position");
        if (position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out int value) || value < 0)
        {
            throw new InvalidDataException($"Baseline {context} constructorBinding position must be a non-negative integer.");
        }

        RequireBoolean(binding, "hasDefaultValue", context);
    }

    private static void ValidateMemberNames(JsonElement memberNames, string context)
    {
        if (memberNames.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Baseline {context} memberNames must be an array.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement name in memberNames.EnumerateArray())
        {
            if (name.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(name.GetString()) || !names.Add(name.GetString()!))
            {
                throw new InvalidDataException($"Baseline {context} memberNames must contain unique non-empty strings.");
            }
        }
    }

    private static bool ValidatePolymorphism(JsonElement polymorphism, string context)
    {
        if (polymorphism.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline {context} polymorphism must be an object.");
        }

        ValidateObjectProperties(
            polymorphism,
            PolymorphismRequired,
            Array.Empty<string>(),
            $"{context} polymorphism");
        RequireString(polymorphism, "discriminatorPropertyName", context, allowEmpty: false);
        RequireString(polymorphism, "unknownDerivedTypeHandling", context, allowEmpty: false);

        JsonElement derivedTypes = polymorphism.GetProperty("derivedTypes");
        if (derivedTypes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Baseline {context} derivedTypes must be an array.");
        }

        bool allSupported = true;
        var discriminators = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement derivedType in derivedTypes.EnumerateArray())
        {
            string discriminator = RequireString(derivedType, "discriminator", context, allowEmpty: true);
            if (!discriminators.Add(discriminator))
            {
                throw new InvalidDataException($"Baseline {context} contains duplicate polymorphic discriminators.");
            }

            allSupported &= ValidateNode(derivedType, includeMembers: true, $"{context} derived type", discriminatorRequired: true);
        }

        return allSupported;
    }

    private static void ValidateEnumWire(JsonElement enumWire, string context)
    {
        if (enumWire.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline {context} enumWire must be an object.");
        }

        foreach (JsonProperty property in enumWire.EnumerateObject())
        {
            if (property.NameEquals("converterType"))
            {
                if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    throw new InvalidDataException($"Baseline {context} enumWire converterType must be a non-empty string.");
                }
            }
            else if (property.NameEquals("integerTokensAccepted"))
            {
                if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException($"Baseline {context} enumWire integerTokensAccepted must be a boolean.");
                }
            }
            else if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
            {
                throw new InvalidDataException($"Baseline {context} enumWire member tokens must be strings or numbers.");
            }
        }
    }

    private static bool ValidateClassification(JsonElement element, string context)
    {
        string rule = RequireString(element, "rule", context, allowEmpty: false);
        bool supported = RequireBoolean(element, "supported", context);
        bool hasReason = element.TryGetProperty("reason", out JsonElement reasonElement);
        if (hasReason && (reasonElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(reasonElement.GetString())))
        {
            throw new InvalidDataException($"Baseline {context} reason must be a non-empty string.");
        }

        if (!RuleCatalog.Contains(rule) || RuleCatalog.IsSupportedRule(rule) != supported || supported == hasReason)
        {
            throw new InvalidDataException($"Baseline {context} has inconsistent support and reason state.");
        }

        return supported;
    }

    private static void ValidateAttributes(JsonElement attributes, string context)
    {
        if (attributes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Baseline {context} declaredAttributes must be an array.");
        }

        foreach (JsonElement attribute in attributes.EnumerateArray())
        {
            if (attribute.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Baseline {context} attribute must be an object.");
            }

            ValidateObjectProperties(attribute, AttributeRequired, Array.Empty<string>(), $"{context} attribute");
            RequireString(attribute, "type", context, allowEmpty: false);
            JsonElement arguments = attribute.GetProperty("arguments");
            if (arguments.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Baseline {context} attribute arguments must be an array.");
            }

            foreach (JsonElement argument in arguments.EnumerateArray())
            {
                if (argument.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"Baseline {context} attribute argument must be an object.");
                }

                ValidateObjectProperties(argument, AttributeArgumentRequired, Array.Empty<string>(), $"{context} attribute argument");
                RequireString(argument, "name", context, allowEmpty: false);
                RequireString(argument, "value", context, allowEmpty: true);
            }
        }
    }

    private static void ValidateObjectProperties(
        JsonElement element,
        IEnumerable<string> required,
        IEnumerable<string> optional,
        string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline {context} must be an object.");
        }

        HashSet<string> allowed = required.Concat(optional).ToHashSet(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new InvalidDataException($"Baseline {context} contains an unknown property.");
            }
        }

        foreach (string propertyName in required)
        {
            if (!element.TryGetProperty(propertyName, out _))
            {
                throw new InvalidDataException($"Baseline {context} is missing required property '{propertyName}'.");
            }
        }
    }

    private static void ValidateRequiredProperties(JsonElement element, IEnumerable<string> required, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Baseline {context} must be an object.");
        }

        foreach (string propertyName in required)
        {
            if (!element.TryGetProperty(propertyName, out _))
            {
                throw new InvalidDataException($"Baseline {context} is missing required property '{propertyName}'.");
            }
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Baseline contains duplicate JSON members.");
                }

                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                RejectDuplicateProperties(child);
            }
        }
    }

    private static string RequireString(JsonElement element, string propertyName, string context, bool allowEmpty)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            (!allowEmpty && string.IsNullOrEmpty(value.GetString())))
        {
            throw new InvalidDataException($"Baseline {context} property '{propertyName}' must be a {(allowEmpty ? "string" : "non-empty string")}.");
        }

        return value.GetString()!;
    }

    private static bool RequireBoolean(JsonElement element, string propertyName, string context)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException($"Baseline {context} property '{propertyName}' must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static JsonDocument ParseJson(byte[] bytes, int maximumDepth)
    {
        try
        {
            return JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    MaxDepth = maximumDepth,
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
        }
        catch (JsonException exception) when (exception.Message.Contains("depth", StringComparison.OrdinalIgnoreCase) ||
                                               exception.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Baseline exceeds the maximum nesting depth of {maximumDepth}.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Baseline is malformed JSON.", exception);
        }
    }
}
