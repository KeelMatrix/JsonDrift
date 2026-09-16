using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// The single recursive metadata traversal. It records every reachable type once, through the discovery
/// source that reached it, and captures the facts a classification may rest on: the metadata resolver, the
/// converters declared or registered for the contract, the shape evidence of every node, the enum wire
/// identities, and the member metadata. Classification reads those records only, so a path the traversal
/// does not record is visible as missing evidence instead of as a supported contract.
/// </summary>
internal static class MetadataTraversal
{
    /// <summary>
    /// Traversal budget for reachable element, key, value, derived-type, constructor, and nested member
    /// types. A contract that nests deeper is recorded as unavailable rather than assumed classifiable.
    /// </summary>
    public const int MaxTraversalDepth = 8;

    /// <summary>
    /// Recorded in place of the wire name of a string enum member when the recorded framework metadata
    /// cannot produce it.
    /// </summary>
    public const string UnresolvedToken = "<unresolved>";

    /// <summary>Records the complete reachable contract graph of <paramref name="contract"/>.</summary>
    public static RecordedNode Record(JsonTypeInfo contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var walk = new Walk(contract.Options);
        return walk.Visit(contract.Type, MetadataSourceKind.ResolverChain, "root", 0, contract);
    }

    /// <summary>
    /// Resolves the member a property was projected from, so a converter attribute declared on the member can
    /// be read even when the metadata provider did not surface it as a member converter.
    /// </summary>
    public static MemberInfo? ResolveMember(Type type, JsonPropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(property);

        const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        MethodInfo? accessor = property.Get?.Method ?? property.Set?.Method;

        if (accessor is not null && accessor.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            string name = accessor.Name[4..];

            MemberInfo? resolved =
                (MemberInfo?)type.GetProperty(name, InstanceMembers) ??
                type.GetField(name, InstanceMembers);

            if (resolved is not null)
            {
                return resolved;
            }
        }

        return
            (MemberInfo?)type.GetProperty(property.Name, InstanceMembers) ??
            type.GetField(property.Name, InstanceMembers);
    }

    private sealed class Walk
    {
        private readonly JsonSerializerOptions options;
        private readonly HashSet<Type> visited = new();
        private readonly Dictionary<Type, RecordedNode> recorded = new();

        public Walk(JsonSerializerOptions options) => this.options = options;

        public RecordedNode Visit(Type declaredType, MetadataSourceKind source, string path, int depth, JsonTypeInfo? known = null)
        {
            TraversalInventory.Ledger.RecordSource(source);

            Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

            if (depth > MaxTraversalDepth)
            {
                return RecordUnavailable(
                    type,
                    source,
                    path,
                    RecordedNodeUnavailableReason.TraversalBudget,
                    known);
            }

            if (recorded.TryGetValue(type, out RecordedNode? alreadyRecorded))
            {
                var reference = new RecordedNode
                {
                    Source = source,
                    Type = type,
                    TypeName = TypeShapes.TypeName(type),
                    Path = path,
                    Kind = RecordedNodeKind.Reference,
                    FrameworkKind = alreadyRecorded.FrameworkKind,
                };

                reference.Reference = alreadyRecorded;
                TraversalInventory.Ledger.RecordNodeKind(reference.Kind);
                return reference;
            }

            JsonTypeInfo info;

            try
            {
                info = known ?? options.GetTypeInfo(type);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
            {
                return RecordUnavailable(type, source, path, RecordedNodeUnavailableReason.MetadataUnavailable, null);
            }

            var node = new RecordedNode
            {
                Source = source,
                Type = type,
                TypeName = TypeShapes.TypeName(type),
                Path = path,
                FrameworkKind = info.Kind,
            };

            visited.Add(type);
            recorded.Add(type, node);

            node.Resolver = RecordResolver(path, info.Options);
            RecordConverterFacts(node, type, info.Options, path);

            node.Kind = info.Kind switch
            {
                JsonTypeInfoKind.Object => RecordObjectShape(node, info, type, depth),
                JsonTypeInfoKind.Enumerable => RecordEnumerableShape(node, info, type, depth),
                JsonTypeInfoKind.Dictionary => RecordDictionaryShape(node, info, type, depth),
                JsonTypeInfoKind.None => RecordScalarShape(node, info, type, path),
            };

            TraversalInventory.Ledger.RecordNodeKind(node.Kind);
            return node;
        }

        private RecordedNode RecordUnavailable(
            Type type,
            MetadataSourceKind source,
            string path,
            RecordedNodeUnavailableReason reason,
            JsonTypeInfo? known)
        {
            var node = new RecordedNode
            {
                Source = source,
                Type = type,
                TypeName = TypeShapes.TypeName(type),
                Path = path,
                Kind = RecordedNodeKind.Unavailable,
                UnavailableReason = reason,
                FrameworkKind = known?.Kind,
            };

            node.Resolver = RecordResolver(path, known?.Options ?? options);

            recorded.TryAdd(type, node);

            TraversalInventory.Ledger.RecordNodeKind(node.Kind);
            return node;
        }

        /// <summary>
        /// Records the shape children of an object contract. Members, constructor parameters, and registered
        /// derived types are enumerated here, so a derived type that is itself a collection, a dictionary, or
        /// another polymorphic base is walked through the same recursion as the root contract.
        /// </summary>
        private RecordedNodeKind RecordObjectShape(RecordedNode node, JsonTypeInfo info, Type type, int depth)
        {
            node.MembersRecorded = true;

            foreach (JsonPropertyInfo property in info.Properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                string memberPath = $"{node.Path} member '{property.Name}'";
                RecordedMember member = RecordMember(info, property, memberPath, depth);
                node.Members.Add(member);

                if (property.IsExtensionData)
                {
                    TraversalInventory.Ledger.RecordSource(MetadataSourceKind.ExtensionData);
                    node.Edges.Add(new RecordedEdge(MetadataSourceKind.ExtensionData, member.Shape));
                }
            }

            RecordConstructorParameters(node, type, depth);
            RecordPolymorphism(node, info, depth);

            return RecordedNodeKind.Object;
        }

        private RecordedNodeKind RecordEnumerableShape(RecordedNode node, JsonTypeInfo info, Type type, int depth)
        {
            Type? elementType = info.ElementType ?? TypeShapes.ElementType(type);

            if (elementType is not null)
            {
                node.ElementTypeRecorded = true;
                node.Edges.Add(new RecordedEdge(
                    MetadataSourceKind.EnumerableElementTypes,
                    Visit(elementType, MetadataSourceKind.EnumerableElementTypes, $"{node.Path} element type", depth + 1)));
            }

            return RecordedNodeKind.Enumerable;
        }

        private RecordedNodeKind RecordDictionaryShape(RecordedNode node, JsonTypeInfo info, Type type, int depth)
        {
            Type? keyType = info.KeyType;
            Type? valueType = info.ElementType;

            if ((keyType is null || valueType is null) &&
                TypeShapes.TryGetDictionaryTypes(type, out Type? shapeKeyType, out Type? shapeValueType))
            {
                keyType ??= shapeKeyType;
                valueType ??= shapeValueType;
            }

            if (keyType is not null)
            {
                node.KeyTypeRecorded = true;
                node.Edges.Add(new RecordedEdge(
                    MetadataSourceKind.DictionaryKeyTypes,
                    Visit(keyType, MetadataSourceKind.DictionaryKeyTypes, $"{node.Path} key type", depth + 1)));
            }

            if (valueType is not null)
            {
                node.ValueTypeRecorded = true;
                node.Edges.Add(new RecordedEdge(
                    MetadataSourceKind.DictionaryValueTypes,
                    Visit(valueType, MetadataSourceKind.DictionaryValueTypes, $"{node.Path} value type", depth + 1)));
            }

            return RecordedNodeKind.Dictionary;
        }

        private static RecordedNodeKind RecordScalarShape(RecordedNode node, JsonTypeInfo info, Type type, string path)
        {
            node.EnumWire = RecordEnumWire(info, null, type, path);
            return RecordedNodeKind.Scalar;
        }

        private void RecordConstructorParameters(RecordedNode node, Type type, int depth)
        {
            ConstructorInfo[] constructors;

            try
            {
                constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
                node.ConstructorsRecorded = false;
                return;
            }

            node.ConstructorsRecorded = true;

            foreach (ConstructorInfo constructor in constructors.OrderBy(
                static constructor => constructor.ToString(),
                StringComparer.Ordinal))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    node.Edges.Add(new RecordedEdge(
                        MetadataSourceKind.ConstructorParameters,
                        Visit(
                            parameter.ParameterType,
                            MetadataSourceKind.ConstructorParameters,
                            $"{node.Path} constructor parameter '{parameter.Name}'",
                            depth + 1)));
                }
            }
        }

        private void RecordPolymorphism(RecordedNode node, JsonTypeInfo info, int depth)
        {
            JsonPolymorphismOptions? polymorphism = info.PolymorphismOptions;

            if (polymorphism is null || polymorphism.DerivedTypes.Count == 0)
            {
                return;
            }

            node.DiscriminatorPropertyName = polymorphism.TypeDiscriminatorPropertyName;
            node.UnknownDerivedTypeHandling = polymorphism.UnknownDerivedTypeHandling.ToString();

            foreach (JsonDerivedType derived in polymorphism.DerivedTypes.OrderBy(
                static derived => derived.TypeDiscriminator?.ToString(),
                StringComparer.Ordinal))
            {
                string discriminator = derived.TypeDiscriminator?.ToString() ?? string.Empty;
                RecordedNode derivedNode = Visit(
                    derived.DerivedType,
                    MetadataSourceKind.PolymorphismDerivedTypes,
                    $"{node.Path} registered derived type '{discriminator}'",
                    depth + 1);

                node.DerivedTypes.Add(new RecordedDerivedType(discriminator, TypeShapes.TypeName(derived.DerivedType), derivedNode));
            }
        }

        private RecordedMember RecordMember(JsonTypeInfo owner, JsonPropertyInfo property, string path, int depth)
        {
            var facts = new List<RecordedConverterFact>();
            MemberInfo? member = ResolveMember(owner.Type, property);
            JsonConverterAttribute? attribute = member?.GetCustomAttribute<JsonConverterAttribute>(inherit: true);

            if (attribute?.ConverterType is Type attributeConverter)
            {
                TraversalInventory.Ledger.RecordSource(MetadataSourceKind.MemberConverterAttribute);
                facts.Add(new RecordedConverterFact(MetadataSourceKind.MemberConverterAttribute, path, property.PropertyType, attributeConverter));
            }

            if (property.CustomConverter is JsonConverter memberConverter)
            {
                TraversalInventory.Ledger.RecordSource(MetadataSourceKind.MemberCustomConverter);
                facts.Add(new RecordedConverterFact(MetadataSourceKind.MemberCustomConverter, path, property.PropertyType, memberConverter.GetType()));
            }

            RecordedNode shape = Visit(property.PropertyType, MetadataSourceKind.ObjectMembers, path, depth + 1);

            bool opaque =
                facts.Any(fact => !ContractAllowlists.IsAllowlistedConverterFor(fact.ConverterType, property.PropertyType)) ||
                shape.ConverterFacts.Any(fact => !ContractAllowlists.IsAllowlistedConverterFor(fact.ConverterType, property.PropertyType));

            return new RecordedMember(
                property.Name,
                property.PropertyType,
                property.IsRequired,
                property.IsGetNullable,
                property.IsSetNullable,
                property.IsExtensionData,
                opaque ? null : RecordEnumWire(owner, property, property.PropertyType, path),
                facts,
                shape)
            {
                Path = path,
                MetadataNotResolved = opaque,
            };
        }

        private static RecordedResolverFact RecordResolver(string path, JsonSerializerOptions options)
        {
            IJsonTypeInfoResolver? resolver = options.TypeInfoResolver;
            int chainLength;

            try
            {
                chainLength = options.TypeInfoResolverChain.Count;
            }
            catch (InvalidOperationException)
            {
                chainLength = resolver is null ? 0 : 1;
            }

            int modifierCount = resolver is DefaultJsonTypeInfoResolver reflection ? reflection.Modifiers.Count : 0;

            return new RecordedResolverFact(path, resolver?.GetType(), chainLength, modifierCount);
        }

        /// <summary>
        /// Records the converters declared on the visited type and registered on the options. A converter is
        /// recorded with its declared type identity, so the classifier decides provenance by assembly
        /// identity rather than by namespace.
        /// </summary>
        private static void RecordConverterFacts(RecordedNode node, Type type, JsonSerializerOptions options, string path)
        {
            if (type.GetCustomAttribute<JsonConverterAttribute>(inherit: true)?.ConverterType is Type attributeConverter)
            {
                TraversalInventory.Ledger.RecordSource(MetadataSourceKind.TypeConverterAttribute);
                node.ConverterFacts.Add(new RecordedConverterFact(MetadataSourceKind.TypeConverterAttribute, path, type, attributeConverter));
            }

            // The recorded order is the declared converter name order, not the registration order, so two
            // option sets that register the same converters in a different order record the same facts.
            foreach (JsonConverter converter in options.Converters.OrderBy(
                static converter => converter.GetType().FullName ?? converter.GetType().Name,
                StringComparer.Ordinal))
            {
                TraversalInventory.Ledger.RecordSource(MetadataSourceKind.OptionsConverters);
                node.ConverterFacts.Add(new RecordedConverterFact(MetadataSourceKind.OptionsConverters, path, type, converter.GetType()));
            }
        }

        /// <summary>
        /// Records the wire identity of an enum contract from the effective framework converter: the converter
        /// declared on the member takes precedence over the converter declared on the enum type, which takes
        /// precedence over a converter registered on the options. Only allowlisted framework converters are
        /// executed; any other converter leaves the wire identity unresolved, which is reported unsupported.
        /// </summary>
        private static RecordedEnumWire? RecordEnumWire(JsonTypeInfo owner, JsonPropertyInfo? property, Type declaredType, string path)
        {
            Type enumType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

            if (!enumType.IsEnum)
            {
                return null;
            }

            if (!TryResolveEnumOptions(owner, property, enumType, out JsonSerializerOptions effectiveOptions))
            {
                return new RecordedEnumWire(
                    false,
                    true,
                    Enum.GetNames(enumType)
                        .OrderBy(static name => name, StringComparer.Ordinal)
                        .Select(static name => new RecordedEnumMember(name, UnresolvedToken, true))
                        .ToArray());
            }

            var members = new List<RecordedEnumMember>();
            bool unresolved = false;
            bool writesStrings = EnumWritesStringTokens(owner, property, enumType);

            foreach (string name in Enum.GetNames(enumType).OrderBy(static name => name, StringComparer.Ordinal))
            {
                string token = WriteEnumToken(effectiveOptions, enumType, name);

                if (string.Equals(token, UnresolvedToken, StringComparison.Ordinal))
                {
                    unresolved = true;
                    members.Add(new RecordedEnumMember(name, UnresolvedToken, true));
                    continue;
                }

                bool isString = token.Length >= 2 && token[0] == '"' && token[^1] == '"';
                writesStrings &= isString;
                members.Add(new RecordedEnumMember(
                    name,
                    isString ? token[1..^1] : token,
                    isString));
            }

            return new RecordedEnumWire(writesStrings, unresolved, members);
        }

        /// <summary>
        /// The options that decide an enum member's wire identity. The converter declared on the member is the
        /// member's effective converter, so it is inserted in front of the contract options; the converter
        /// declared on the enum type and the converters registered on the options are applied by the
        /// framework itself when the enum type metadata is resolved.
        /// </summary>
        private static bool TryResolveEnumOptions(
            JsonTypeInfo owner,
            JsonPropertyInfo? property,
            Type enumType,
            out JsonSerializerOptions effectiveOptions)
        {
            effectiveOptions = owner.Options;

            if (owner.Options.Converters.Any(converter => !ContractAllowlists.IsAllowlistedConverterType(converter.GetType())))
            {
                return false;
            }

            if (TypeConverterAttribute(enumType) is Type declaredType &&
                !ContractAllowlists.IsAllowlistedConverterFor(declaredType, enumType))
            {
                return false;
            }

            JsonConverter? memberConverter = MemberEnumConverter(owner, property, enumType);

            if (memberConverter is null)
            {
                return true;
            }

            if (!ContractAllowlists.IsAllowlistedConverterFor(memberConverter.GetType(), enumType))
            {
                return false;
            }

            var copy = new JsonSerializerOptions(owner.Options);
            copy.Converters.Insert(0, memberConverter);
            effectiveOptions = copy;
            return true;
        }

        /// <summary>
        /// The converter declared on the member: the converter the metadata provider assigned to the property,
        /// or the converter of the <c>JsonConverterAttribute</c> declared on the member itself.
        /// </summary>
        private static JsonConverter? MemberEnumConverter(JsonTypeInfo owner, JsonPropertyInfo? property, Type enumType)
        {
            if (property is null)
            {
                return null;
            }

            if (property.CustomConverter is JsonConverter assigned)
            {
                return assigned;
            }

            MemberInfo? member = MetadataTraversal.ResolveMember(owner.Type, property);
            Type? converterType = member?.GetCustomAttribute<JsonConverterAttribute>(inherit: true)?.ConverterType;

            if (converterType is null ||
                !ContractAllowlists.IsAllowlistedConverterFor(converterType, enumType))
            {
                return null;
            }

            try
            {
                return (JsonConverter?)Activator.CreateInstance(converterType);
            }
            catch (Exception exception) when (exception is MissingMethodException or MemberAccessException or TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>
        /// True when the recorded metadata shows that an enum contract is written as JSON strings. The
        /// converter declared on the member takes precedence over the converter declared on the enum type,
        /// which takes precedence over a converter registered on the options; without any of them the
        /// framework writes numbers.
        /// </summary>
        private static bool EnumWritesStringTokens(JsonTypeInfo owner, JsonPropertyInfo? property, Type enumType)
        {
            if (MemberEnumConverter(owner, property, enumType)?.GetType() is Type memberConverter)
            {
                return WritesStringTokens(memberConverter);
            }

            if (TypeConverterAttribute(enumType) is Type declaredType)
            {
                return WritesStringTokens(declaredType);
            }

            foreach (JsonConverter converter in owner.Options.Converters)
            {
                if (converter is JsonStringEnumConverter)
                {
                    return true;
                }
            }

            return false;
        }

        private static Type? TypeConverterAttribute(Type type) =>
            type.GetCustomAttribute<JsonConverterAttribute>(inherit: true)?.ConverterType;

        private static bool WritesStringTokens(Type converterType) =>
            converterType == typeof(JsonStringEnumConverter) ||
            (converterType.IsGenericType &&
             converterType.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>));

        private static string WriteEnumToken(JsonSerializerOptions options, Type enumType, string name)
        {
            try
            {
                JsonTypeInfo info = options.GetTypeInfo(enumType);
                object value = Enum.Parse(enumType, name);
                return JsonSerializer.Serialize(value, info);
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
            {
                return UnresolvedToken;
            }
        }
    }

}
