using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// Produces a deterministic structural description of an effective serializer contract from the recorded
/// traversal output. Output rules: members sorted by name, fixed key order, LF newlines, UTF-8 without a
/// byte order mark, no timestamps, no absolute paths, and repeated visits described by reference so
/// recursive graphs terminate. The support verdicts of the document are the classification of the same
/// recorded nodes, so the document and the classification can never disagree.
/// </summary>
internal static class ContractCanonicalizer
{
    /// <summary>The canonical baseline document format version.</summary>
    public const int FormatVersion = 2;

    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Canonicalize(JsonTypeInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        RecordedNode root = MetadataTraversal.Record(contract);
        Classification verdict = ContractClassifier.Classify(root);

        var document = new JsonObject
        {
            ["formatVersion"] = FormatVersion,
            ["options"] = DescribeOptions(root.Options),
            ["root"] = DescribeNode(root, includeMembers: true),
        };

        // The aggregate flag restates the whole-contract verdict at document level, so a report layer never
        // has to walk the document to find out whether nested metadata is classifiable.
        document["overallSupported"] = verdict.Supported;

        string json = document.ToJsonString(WriterOptions);
        string normalized = json.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : string.Concat(normalized, "\n");
    }

    /// <summary>
    /// Describes the wire-affecting serializer option values the contract was recorded under, in the fixed
    /// order of the option families. The values are part of the document, so a contract that keeps its shape
    /// and changes an option value the allowlist accepts is a document difference instead of a pair of
    /// byte-identical documents.
    /// </summary>
    private static JsonObject DescribeOptions(RecordedOptionSet options)
    {
        var described = new JsonObject();

        foreach (RecordedOptionValue value in options.Values)
        {
            described[value.Id] = value.Value;
        }

        return described;
    }

    /// <summary>
    /// Describes one recorded node. Every reachable child node is described with its complete metadata unless
    /// the traversal recorded a reference, so nested objects, collection elements, and dictionary values remain
    /// comparable while recursive graphs still terminate.
    /// </summary>
    private static JsonObject DescribeNode(RecordedNode node, bool includeMembers)
    {
        Classification verdict = ContractClassifier.Classify(node);

        var described = new JsonObject
        {
            ["typeName"] = node.TypeName,
            ["kind"] = KindName(node.Kind),
            ["reachedBy"] = MetadataSourceRules.Id(node.Source),
            ["path"] = node.Path,
            ["rule"] = verdict.RuleId,
            ["supported"] = verdict.Supported,
            ["declaredAttributes"] = DescribeAttributes(node.DeclaredAttributes),
        };

        if (!verdict.Supported && verdict.Reason is not null)
        {
            described["reason"] = verdict.Reason;
        }

        switch (node.Kind)
        {
            case RecordedNodeKind.Object:
                described["extensionData"] = node.Members.Any(static member => member.ExtensionData);

                if (includeMembers)
                {
                    var members = new JsonArray();

                    foreach (RecordedMember member in node.Members.OrderBy(static member => member.Name, StringComparer.Ordinal))
                    {
                        members.Add(DescribeMember(member));
                    }

                    described["members"] = members;
                }
                else
                {
                    var names = new JsonArray();

                    foreach (string name in node.Members
                        .Where(static member => !member.ExtensionData)
                        .Select(static member => member.Name)
                        .OrderBy(static name => name, StringComparer.Ordinal))
                    {
                        names.Add(name);
                    }

                    described["memberNames"] = names;
                }

                if (node.DerivedTypes.Count > 0)
                {
                    described["polymorphism"] = DescribePolymorphism(node);
                }

                break;

            case RecordedNodeKind.Enumerable:
                if (EdgeNode(node, MetadataSourceKind.EnumerableElementTypes) is RecordedNode element)
                {
                    described["elementType"] = element.TypeName;
                    described["element"] = DescribeNode(element, includeMembers: true);
                }

                break;

            case RecordedNodeKind.Dictionary:
                if (EdgeNode(node, MetadataSourceKind.DictionaryKeyTypes) is RecordedNode key)
                {
                    described["keyType"] = key.TypeName;
                    described["key"] = DescribeNode(key, includeMembers: true);
                }

                if (EdgeNode(node, MetadataSourceKind.DictionaryValueTypes) is RecordedNode value)
                {
                    described["valueType"] = value.TypeName;
                    described["value"] = DescribeNode(value, includeMembers: true);
                }

                break;

            case RecordedNodeKind.Scalar:
                described["tokenKind"] = node.EnumWire is { Unresolved: false, WritesStringTokens: true }
                    ? "string"
                    : TypeShapes.TokenKind(node.Type ?? typeof(object));
                if (node.EnumWire is RecordedEnumWire wire)
                {
                    described["enumWire"] = DescribeEnumWire(wire);
                }

                break;

            case RecordedNodeKind.Reference:
                described["reference"] = node.Reference?.Path ?? string.Empty;
                break;

            case RecordedNodeKind.Unavailable:
                break;
        }

        return described;
    }

    private static JsonObject DescribeMember(RecordedMember member)
    {
        Classification verdict = ContractClassifier.Classify(member);

        var described = new JsonObject
        {
            ["name"] = member.Name,
            ["declaredType"] = TypeShapes.TypeName(member.DeclaredType),
            ["tokenKind"] = TokenKind(member, verdict),
            ["required"] = member.Required,
            ["getNullable"] = member.GetNullable,
            ["setNullable"] = member.SetNullable,
            ["extensionData"] = member.ExtensionData,
            ["included"] = member.Included,
            ["declaredAttributes"] = DescribeAttributes(member.DeclaredAttributes),
            ["rule"] = verdict.RuleId,
            ["supported"] = verdict.Supported,
        };

        if (member.ConstructorBinding is RecordedConstructorBinding binding)
        {
            described["constructorBinding"] = new JsonObject
            {
                ["name"] = binding.Name,
                ["position"] = binding.Position,
                ["hasDefaultValue"] = binding.HasDefaultValue,
            };
        }

        if (!verdict.Supported && verdict.Reason is not null)
        {
            described["reason"] = verdict.Reason;
        }

        if (member.Included && member.EnumWire is RecordedEnumWire wire)
        {
            described["enumWire"] = DescribeEnumWire(wire);
        }

        // A member whose metadata is decided by an unrecognized converter records no shape, and a scalar
        // member has no nested contract to describe, so only a classifiable complex shape is recorded.
        if (member.Included && !member.MetadataNotResolved && IsComplexShape(member.Shape))
        {
            described["shape"] = DescribeNode(member.Shape, includeMembers: true);
        }

        return described;
    }

    private static JsonObject DescribePolymorphism(RecordedNode node)
    {
        var derivedTypes = new JsonArray();

        foreach (RecordedDerivedType derived in node.DerivedTypes)
        {
            JsonObject descriptor = DescribeNode(derived.Node, includeMembers: true);
            descriptor.Insert(0, "discriminator", derived.Discriminator);
            derivedTypes.Add(descriptor);
        }

        return new JsonObject
        {
            ["discriminatorPropertyName"] = node.DiscriminatorPropertyName,
            ["unknownDerivedTypeHandling"] = node.UnknownDerivedTypeHandling,
            ["derivedTypes"] = derivedTypes,
        };
    }

    private static JsonObject DescribeEnumWire(RecordedEnumWire wire)
    {
        var identity = new JsonObject();

        if (wire.ConverterConfiguration is RecordedConverterConfiguration configuration)
        {
            identity["converterType"] = configuration.ConverterType;
            identity["integerTokensAccepted"] = configuration.IntegerTokensAccepted;
        }

        foreach (RecordedEnumMember member in wire.Members)
        {
            identity[member.Name] = member.IsString ? JsonValue.Create(member.Token) : NumericToken(member.Token);
        }

        return identity;
    }

    private static JsonArray DescribeAttributes(IReadOnlyList<RecordedAttributeFact> attributes)
    {
        var described = new JsonArray();

        foreach (RecordedAttributeFact attribute in attributes
            .OrderBy(static attribute => attribute.AttributeType, StringComparer.Ordinal)
            .ThenBy(static attribute => attribute.Display, StringComparer.Ordinal))
        {
            var arguments = new JsonArray();

            foreach (RecordedAttributeArgument argument in attribute.Arguments)
            {
                arguments.Add(new JsonObject
                {
                    ["name"] = argument.Name,
                    ["value"] = argument.Value,
                });
            }

            described.Add(new JsonObject
            {
                ["type"] = attribute.AttributeType,
                ["arguments"] = arguments,
            });
        }

        return described;
    }

    private static JsonValue? NumericToken(string token) =>
        long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed)
            ? JsonValue.Create(signed)
            : ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsigned)
                ? JsonValue.Create(unsigned)
                : JsonValue.Create(token);

    private static string TokenKind(RecordedMember member, Classification verdict)
    {
        if (!verdict.Supported)
        {
            return "opaque";
        }

        if (member.EnumWire?.WritesStringTokens == true)
        {
            return "string";
        }

        return TypeShapes.TokenKind(member.DeclaredType);
    }

    private static bool IsComplexShape(RecordedNode node) => node.Kind switch
    {
        RecordedNodeKind.Object => true,
        RecordedNodeKind.Enumerable => true,
        RecordedNodeKind.Dictionary => true,
        RecordedNodeKind.Scalar => false,
        RecordedNodeKind.Reference => node.Reference is not null && IsComplexShape(node.Reference),
        RecordedNodeKind.Unavailable => false,
    };

    private static RecordedNode? EdgeNode(RecordedNode node, MetadataSourceKind source)
    {
        foreach (RecordedEdge edge in node.Edges)
        {
            if (edge.Source == source)
            {
                return edge.Node;
            }
        }

        return null;
    }

    /// <summary>
    /// The recorded kind of a node in the canonical document. The switch is exhaustive, so a node kind added
    /// without a documented name fails the Release build.
    /// </summary>
    private static string KindName(RecordedNodeKind kind) => kind switch
    {
        RecordedNodeKind.Object => "object",
        RecordedNodeKind.Enumerable => "array",
        RecordedNodeKind.Dictionary => "dictionary",
        RecordedNodeKind.Scalar => "scalar",
        RecordedNodeKind.Reference => "reference",
        RecordedNodeKind.Unavailable => "unavailable",
    };
}
