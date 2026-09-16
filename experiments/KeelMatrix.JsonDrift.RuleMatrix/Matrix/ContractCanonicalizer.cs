using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Produces a deterministic structural description of an effective serializer contract from the recorded
/// traversal output. Output rules: members sorted by name, fixed key order, LF newlines, UTF-8 without a
/// byte order mark, no timestamps, no absolute paths, and repeated visits described by reference so
/// recursive graphs terminate. The support verdicts of the document are the classification of the same
/// recorded nodes, so the document and the classification can never disagree.
/// </summary>
internal static class ContractCanonicalizer
{
    /// <summary>The canonical document format version.</summary>
    public const int ContractVersion = 2;

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
            ["contractVersion"] = ContractVersion,
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
    /// Describes one recorded node. Object contracts record their full member content when
    /// <paramref name="includeMembers"/> is true and a member-name summary otherwise, so a nested shape stays
    /// readable while the root and registered derived types record the complete member metadata.
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
                if (EdgeTypeName(node, MetadataSourceKind.EnumerableElementTypes) is string elementType)
                {
                    described["elementType"] = elementType;
                }

                break;

            case RecordedNodeKind.Dictionary:
                if (EdgeTypeName(node, MetadataSourceKind.DictionaryKeyTypes) is string keyType)
                {
                    described["keyType"] = keyType;
                }

                if (EdgeTypeName(node, MetadataSourceKind.DictionaryValueTypes) is string valueType)
                {
                    described["valueType"] = valueType;
                }

                break;

            case RecordedNodeKind.Scalar:
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
            ["rule"] = verdict.RuleId,
            ["supported"] = verdict.Supported,
        };

        if (!verdict.Supported && verdict.Reason is not null)
        {
            described["reason"] = verdict.Reason;
        }

        if (member.EnumWire is RecordedEnumWire wire)
        {
            described["enumWire"] = DescribeEnumWire(wire);
        }

        // A member whose metadata is decided by an unrecognized converter records no shape, and a scalar
        // member has no nested contract to describe, so only a classifiable complex shape is recorded.
        if (!member.MetadataNotResolved && IsComplexShape(member.Shape))
        {
            described["shape"] = DescribeNode(member.Shape, includeMembers: false);
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

        foreach (RecordedEnumMember member in wire.Members)
        {
            identity[member.Name] = member.IsString ? JsonValue.Create(member.Token) : NumericToken(member.Token);
        }

        return identity;
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
        RecordedNodeKind.Reference => false,
        RecordedNodeKind.Unavailable => false,
    };

    private static string? EdgeTypeName(RecordedNode node, MetadataSourceKind source)
    {
        foreach (RecordedEdge edge in node.Edges)
        {
            if (edge.Source == source)
            {
                return edge.Node.TypeName;
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
