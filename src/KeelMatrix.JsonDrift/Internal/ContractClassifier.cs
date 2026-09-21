using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// Classifies a recorded contract. A node is supported only when its recorded shape evidence is complete and
/// its recorded converter and resolver metadata matches the positive allowlists of
/// <see cref="ContractAllowlists"/>; everything else is reported unsupported. Classification reads the
/// recorded nodes only and never resolves metadata again, so it cannot disagree with what the traversal
/// recorded, and a metadata path the traversal missed shows up as missing evidence.
/// </summary>
internal static class ContractClassifier
{
    /// <summary>Classifies the record of one contract and returns the reason when it is unsupported.</summary>
    public static string? DescribeUnsupported(JsonTypeInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        Classification classification = Classify(MetadataTraversal.Record(contract));
        return classification.Supported ? null : classification.Reason;
    }

    /// <summary>Whether the record of one contract is fully classifiable.</summary>
    public static bool IsSupported(JsonTypeInfo contract) => DescribeUnsupported(contract) is null;

    /// <summary>Classifies one recorded node, or one recorded member.</summary>
    public static Classification Classify(RecordedNode node) => new Session().ClassifyNode(node);

    public static Classification Classify(RecordedMember member) => new Session().ClassifyMember(member);

    /// <summary>
    /// One classification run. The session memoizes verdicts and tracks the nodes it is currently
    /// classifying, so a recursive contract terminates: a type that refers to itself while its own record is
    /// still being classified is provisional until the containing node reports its verdict, which is where
    /// an unsupported fact inside the cycle is detected.
    /// </summary>
    private sealed class Session
    {
        private readonly HashSet<RecordedNode> inProgress = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<RecordedNode, Classification> verdicts = new(ReferenceEqualityComparer.Instance);

        public Classification ClassifyNode(RecordedNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            if (verdicts.TryGetValue(node, out Classification? cached) && cached is not null)
            {
                return cached;
            }

            if (!inProgress.Add(node))
            {
                return Classification.Classifiable(RuleIds.SupportedReference);
            }

            Classification verdict = node.Kind switch
            {
                RecordedNodeKind.Object => ClassifyObject(node),
                RecordedNodeKind.Enumerable => ClassifyEnumerable(node),
                RecordedNodeKind.Dictionary => ClassifyDictionary(node),
                RecordedNodeKind.Scalar => ClassifyScalar(node),
                RecordedNodeKind.Reference => ClassifyReference(node),
                RecordedNodeKind.Unavailable => ClassifyUnavailable(node),
            };

            inProgress.Remove(node);

            if (node.Kind != RecordedNodeKind.Reference)
            {
                verdicts[node] = verdict;
            }

            TraversalInventory.Ledger.RecordRule(verdict.RuleId);
            return verdict;
        }

        public Classification ClassifyMember(RecordedMember member)
        {
            ArgumentNullException.ThrowIfNull(member);

            Classification verdict = ClassifyMemberCore(member);
            TraversalInventory.Ledger.RecordRule(verdict.RuleId);
            return verdict;
        }

        private Classification ClassifyObject(RecordedNode node)
        {
            if (Gate(node) is Classification gate)
            {
                return gate;
            }

            if (!node.MembersRecorded)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(MetadataSourceKind.ObjectMembers, $"{node.Path} recorded no member metadata"));
            }

            if (!node.ConstructorsRecorded)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(MetadataSourceKind.ConstructorParameters, $"{node.Path} recorded no constructor metadata"));
            }

            foreach (RecordedMember member in node.Members)
            {
                if (!member.Included)
                {
                    continue;
                }

                Classification memberVerdict = ClassifyMemberCore(member);

                if (!memberVerdict.Supported)
                {
                    return memberVerdict;
                }
            }

            foreach (RecordedEdge edge in node.Edges)
            {
                Classification edgeVerdict = ClassifyNode(edge.Node);

                if (!edgeVerdict.Supported)
                {
                    return edgeVerdict;
                }
            }

            foreach (RecordedDerivedType derived in node.DerivedTypes)
            {
                Classification derivedVerdict = ClassifyNode(derived.Node);

                if (!derivedVerdict.Supported)
                {
                    return derivedVerdict;
                }
            }

            return Classification.Classifiable(RuleIds.SupportedObject);
        }

        private Classification ClassifyEnumerable(RecordedNode node)
        {
            if (Gate(node) is Classification gate)
            {
                return gate;
            }

            if (!node.ElementTypeRecorded)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(
                        MetadataSourceKind.EnumerableElementTypes,
                        $"{node.Path} is enumerable but recorded no element type"));
            }

            if (node.CollectionSemantics != RecordedCollectionSemantics.OrderedWithMultiplicity)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedCollectionSemanticsUnproven,
                    Witness(
                        MetadataSourceKind.EnumerableElementTypes,
                        $"{node.Path} uses {node.TypeName}, whose materializer has no measured order-and-multiplicity preservation rule"));
            }

            return ClassifyEdges(node, RuleIds.SupportedEnumerable);
        }

        private Classification ClassifyDictionary(RecordedNode node)
        {
            if (Gate(node) is Classification gate)
            {
                return gate;
            }

            if (!node.KeyTypeRecorded)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(MetadataSourceKind.DictionaryKeyTypes, $"{node.Path} is a dictionary but recorded no key type"));
            }

            if (!node.ValueTypeRecorded)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(
                        MetadataSourceKind.DictionaryValueTypes,
                        $"{node.Path} is a dictionary but recorded no value type"));
            }

            return ClassifyEdges(node, RuleIds.SupportedDictionary);
        }

        private Classification ClassifyEdges(RecordedNode node, string supportedRule)
        {
            foreach (RecordedEdge edge in node.Edges)
            {
                Classification edgeVerdict = ClassifyNode(edge.Node);

                if (!edgeVerdict.Supported)
                {
                    return edgeVerdict;
                }
            }

            return Classification.Classifiable(supportedRule);
        }

        private static Classification ClassifyScalar(RecordedNode node)
        {
            if (Gate(node) is Classification gate)
            {
                return gate;
            }

            if (node.Type is not Type type)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(node.Source, $"{node.Path} recorded no type"));
            }

            Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            if (effectiveType.IsEnum)
            {
                if (node.EnumWire is null)
                {
                    return Unclassifiable(
                        RuleIds.UnsupportedShapeEvidenceMissing,
                        Witness(node.Source, $"{node.Path} recorded no enum wire identity"));
                }

                if (node.EnumWire.Unresolved)
                {
                    return Unclassifiable(
                        RuleIds.UnsupportedEnumWireUnresolved,
                        Witness(
                            node.Source,
                            $"{node.Path} declares an enum converter whose wire name could not be produced"));
                }

                if (ClassifyConverterConfiguration(node.EnumWire, node.Source, node.Path) is Classification configurationVerdict)
                {
                    return configurationVerdict;
                }

                return Classification.Classifiable(RuleIds.SupportedEnum);
            }

            if (ContractAllowlists.IsAllowlistedScalar(type))
            {
                return Classification.Classifiable(RuleIds.SupportedScalar);
            }

            return Unclassifiable(
                RuleIds.UnsupportedScalarUnlisted,
                Witness(
                    node.Source,
                    $"{node.Path} has scalar type {TypeShapes.TypeName(type)}, which is not on the framework scalar allowlist"));
        }

        private Classification ClassifyReference(RecordedNode node)
        {
            if (node.Reference is not RecordedNode target)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedMetadataUnavailable,
                    Witness(node.Source, $"{node.Path} refers to a node that was not recorded"));
            }

            Classification verdict = ClassifyNode(target);

            return verdict.Supported ? Classification.Classifiable(RuleIds.SupportedReference) : verdict;
        }

        private static Classification ClassifyUnavailable(RecordedNode node) => node.UnavailableReason switch
        {
            RecordedNodeUnavailableReason.TraversalBudget => Unclassifiable(
                RuleIds.UnsupportedTraversalBudget,
                Witness(
                    node.Source,
                    $"{node.Path} nests deeper than the classification traversal budget ({MetadataTraversal.MaxTraversalDepth})")),
            RecordedNodeUnavailableReason.MetadataUnavailable => Unclassifiable(
                RuleIds.UnsupportedMetadataUnavailable,
                Witness(node.Source, $"{node.Path} has no metadata for these options")),
            null => Unclassifiable(
                RuleIds.UnsupportedMetadataUnavailable,
                Witness(node.Source, $"{node.Path} was recorded without an availability reason")),
        };

        private Classification ClassifyMemberCore(RecordedMember member)
        {
            if (!member.Included)
            {
                return Classification.Classifiable(RuleIds.SupportedMember);
            }

            if (member.CanSerialize && !member.CanDeserialize)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedMemberMaterializationUnproven,
                    Witness(
                        MetadataSourceKind.ObjectMembers,
                        $"{member.Path} can write a JSON value but has no setter or constructor binding that can restore it"));
            }

            foreach (RecordedConverterFact fact in member.ConverterFacts)
            {
                if (DescribeUnallowlisted(fact, member.DeclaredType) is Classification verdict)
                {
                    return verdict;
                }
            }

            if (member.EnumWire is RecordedEnumWire wire && wire.Unresolved)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedEnumWireUnresolved,
                    Witness(
                        member.ConverterFacts.Count > 0
                            ? member.ConverterFacts[0].Source
                            : MetadataSourceKind.TypeConverterAttribute,
                        $"{member.Path} declares an enum converter whose wire name could not be produced"));
            }

            if (DescribeUnlistedAttributes(member.DeclaredAttributes) is Classification attributeVerdict)
            {
                return attributeVerdict;
            }

            if (member.EnumWire is RecordedEnumWire memberWire &&
                ClassifyConverterConfiguration(
                    memberWire,
                    member.ConverterFacts.Count > 0 ? member.ConverterFacts[0].Source : MetadataSourceKind.ConverterConfiguration,
                    member.Path) is Classification memberConfigurationVerdict)
            {
                return memberConfigurationVerdict;
            }

            Classification shapeVerdict = ClassifyNode(member.Shape);

            return shapeVerdict.Supported
                ? Classification.Classifiable(RuleIds.SupportedMember)
                : shapeVerdict;
        }

        /// <summary>
        /// The resolver and converter checks every node has to pass before its shape is considered. A
        /// resolver that is not a recognized framework metadata source, a resolver chain, resolver modifiers,
        /// a converter that is not an allowlisted framework converter, or a serializer option value the
        /// committed checks were not measured under all make the node unsupported.
        /// </summary>
        private static Classification? Gate(RecordedNode node)
        {
            if (DescribeUnlistedOptions(node.Options) is Classification optionVerdict)
            {
                return optionVerdict;
            }

            if (node.Resolver is not RecordedResolverFact resolver)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedResolverUnrecognized,
                    Witness(MetadataSourceKind.ResolverChain, $"{node.Path} recorded no metadata resolver"));
            }

            if (ClassifyResolver(resolver, node) is Classification resolverVerdict)
            {
                return resolverVerdict;
            }

            foreach (RecordedConverterFact fact in node.ConverterFacts)
            {
                if (DescribeUnallowlisted(fact, node.Type ?? fact.TargetType) is Classification converterVerdict)
                {
                    return converterVerdict;
                }
            }

            if (DescribeUnlistedAttributes(node.DeclaredAttributes) is Classification attributeVerdict)
            {
                return attributeVerdict;
            }

            return null;
        }

        private static Classification? DescribeUnlistedAttributes(IReadOnlyList<RecordedAttributeFact> attributes)
        {
            string[] unlisted = attributes
                .Where(static attribute => !ContractAllowlists.IsAllowlistedAttribute(attribute))
                .Select(static attribute => attribute.Display)
                .ToArray();

            if (unlisted.Length == 0)
            {
                foreach (RecordedAttributeFact attribute in attributes)
                {
                    TraversalInventory.Ledger.RecordAcceptedAttribute(attribute);
                }

                return null;
            }

            return Unclassifiable(
                RuleIds.UnsupportedAttributeUnlisted,
                Witness(
                    MetadataSourceKind.DeclaredAttributes,
                    $"the declared serialization attributes {string.Join(", ", unlisted)} are not on the attribute allowlist"));
        }

        private static Classification? ClassifyConverterConfiguration(
            RecordedEnumWire wire,
            MetadataSourceKind source,
            string path)
        {
            if (wire.ConverterConfiguration is not RecordedConverterConfiguration configuration)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedEnumWireUnresolved,
                    Witness(MetadataSourceKind.ConverterConfiguration, $"{path} recorded no converter configuration"));
            }

            TraversalInventory.Ledger.RecordSource(MetadataSourceKind.ConverterConfiguration);

            if (configuration.IntegerTokensAccepted is null)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedEnumWireUnresolved,
                    Witness(
                        MetadataSourceKind.ConverterConfiguration,
                        $"{path} converter configuration {configuration.Display} could not be probed"));
            }

            if (!ContractAllowlists.IsAllowlistedConverterConfiguration(configuration))
            {
                return Unclassifiable(
                    RuleIds.UnsupportedConverterConfigurationUnlisted,
                    Witness(
                        MetadataSourceKind.ConverterConfiguration,
                        $"{path} converter configuration {configuration.Display} is not on the measured allowlist"));
            }

            TraversalInventory.Ledger.RecordAcceptedConverterConfiguration(configuration);
            return null;
        }

        /// <summary>
        /// The reason a recorded option set cannot be classified, or null when every recorded value is one the
        /// committed checks are measured under. An option family that was not recorded at all is missing
        /// evidence rather than an accepted value, so a contract whose option set the traversal failed to
        /// record cannot produce a supported verdict.
        /// </summary>
        private static Classification? DescribeUnlistedOptions(RecordedOptionSet options)
        {
            if (options.IsEmpty)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedShapeEvidenceMissing,
                    Witness(
                        MetadataSourceKind.OptionsSettings,
                        "the contract recorded no serializer option values"));
            }

            string[] unlisted = options.Values
                .Where(value => !ContractAllowlists.IsAllowlistedOptionValue(value.Kind, value.Value))
                .Select(static value => value.Display)
                .ToArray();

            if (unlisted.Length == 0)
            {
                foreach (RecordedOptionValue value in options.Values)
                {
                    TraversalInventory.Ledger.RecordAcceptedOptionValue(value);
                }

                return null;
            }

            return Unclassifiable(
                RuleIds.UnsupportedOptionUnlisted,
                Witness(
                    MetadataSourceKind.OptionsSettings,
                    $"the contract options set {string.Join(", ", unlisted)}, which the serializer option allowlist does not accept"));
        }

        private static Classification? ClassifyResolver(RecordedResolverFact resolver, RecordedNode node)
        {
            if (resolver.ChainLength != 1)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedResolverChain,
                    Witness(
                        MetadataSourceKind.ResolverChain,
                        $"{node.Path} is resolved through {resolver.ChainLength} metadata resolvers"));
            }

            if (resolver.ModifierCount > 0)
            {
                return Unclassifiable(
                    RuleIds.UnsupportedResolverModifiers,
                    Witness(
                        MetadataSourceKind.ResolverChain,
                        $"{node.Path} is resolved by a default reflection resolver with {resolver.ModifierCount} metadata modifiers"));
            }

            if (ContractAllowlists.IsAllowlistedResolver(resolver.ResolverType, resolver.ChainLength, resolver.ModifierCount))
            {
                return null;
            }

            return Unclassifiable(
                RuleIds.UnsupportedResolverUnrecognized,
                Witness(
                    MetadataSourceKind.ResolverChain,
                    $"{node.Path} was produced by unrecognized metadata resolver {resolver.ResolverType?.FullName ?? "<none>"}"));
        }

        /// <summary>
        /// The reason a recorded converter fact cannot be classified, or null when the fact is an allowlisted
        /// framework converter for the recorded target type. Converter provenance is decided from the
        /// recorded assembly identity, so a converter in the framework assembly that the allowlist does not
        /// name is reported separately from a converter that is not framework metadata at all, and a
        /// namespace prefix is never treated as evidence of framework provenance.
        /// </summary>
        private static Classification? DescribeUnallowlisted(RecordedConverterFact fact, Type targetType)
        {
            if (ContractAllowlists.IsAllowlistedConverterFor(fact.ConverterType, targetType))
            {
                return null;
            }

            return ContractAllowlists.IsFrameworkConverter(fact.ConverterType)
                ? Unclassifiable(
                    RuleIds.UnsupportedConverterUnlisted,
                    Witness(
                        fact.Source,
                        $"{fact.Path} is converted by framework converter {fact.ConverterType.FullName}, which the allowlist does not accept for {TypeShapes.TypeName(targetType)}"))
                : Unclassifiable(
                    RuleIds.UnsupportedConverterUnrecognized,
                    Witness(
                        fact.Source,
                        $"{fact.Path} is converted by unrecognized converter {fact.ConverterType.FullName}"));
        }
    }

    private static Classification Unclassifiable(string ruleId, string reason) =>
        Classification.Unclassifiable(ruleId, reason);

    private static string Witness(MetadataSourceKind source, string reason) =>
        ContractDiagnostics.Witness(MetadataSourceRules.Id(source), reason);
}
