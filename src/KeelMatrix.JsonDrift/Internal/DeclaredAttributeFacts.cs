using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.Internal;

/// <summary>
/// Reads declared JSON serialization attributes without naming individual attribute types in the walk. The
/// runtime inventory is every attribute type in the loaded System.Text.Json.Serialization namespace, rather
/// than only the types that currently derive from JsonAttribute. A value that the measured attribute allowlist
/// does not name remains opaque to classification.
/// </summary>
internal static class DeclaredAttributeFacts
{
    /// <summary>
    /// Contract surfaces whose declared attributes are measured as accepted by committed checks. The list is
    /// the single source from which the attribute allowlist is derived; adding a profile without an executed
    /// check is caught by the attribute allowlist inventory gate.
    /// </summary>
    public static IReadOnlyList<Type> MeasuredTypes { get; } = new[]
    {
        typeof(AllowlistedAttributeProbe),
        typeof(AttributeProbePolymorphicRoot),
        typeof(AttributeProbePolymorphicKindRoot),
    };

    /// <summary>
    /// Additional contract surfaces whose declarations are inventoried for the runtime predicate-coverage
    /// gate. They are deliberately separate from <see cref="MeasuredTypes"/>: a probe can establish that a
    /// declaration is recorded and denied without widening the accepted attribute allowlist.
    /// </summary>
    public static IReadOnlyList<Type> InventoryTypes { get; } = MeasuredTypes
        .Concat(new[] { typeof(InventoryAttributeProbe), typeof(InventoryAttributeEnum), typeof(UnmappedAttributeProbe), typeof(AttributeProbeContext) })
        .Distinct()
        .OrderBy(TypeShapes.TypeName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// The only runtime declaration intentionally excluded from the inventory: the abstract JsonAttribute
    /// base cannot itself be applied to a contract declaration. The runtime type and its abstract state are
    /// checked by the D06 declaration-coverage gate, so a future runtime cannot turn this into a silent skip.
    /// </summary>
    public static IReadOnlyList<DeclaredAttributeExclusion> ExplicitExclusions { get; } = new[]
    {
        new DeclaredAttributeExclusion(
            typeof(JsonAttribute),
            $"abstract={typeof(JsonAttribute).IsAbstract}; the base class cannot be applied directly to a contract declaration"),
    };

    /// <summary>All attribute types declared in the loaded System.Text.Json.Serialization namespace.</summary>
    public static IReadOnlyList<Type> RuntimeDeclarationTypes { get; } = typeof(JsonSerializer).Assembly
        .GetTypes()
        .Where(static type =>
            type.IsClass &&
            type.Namespace == "System.Text.Json.Serialization" &&
            typeof(Attribute).IsAssignableFrom(type))
        .OrderBy(TypeShapes.TypeName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The exact declared attribute facts produced by the measured contract surfaces.</summary>
    public static IReadOnlyList<RecordedAttributeFact> MeasuredValues { get; } = MeasuredTypes
        .SelectMany(ReadSurface)
        .DistinctBy(static fact => fact.Display, StringComparer.Ordinal)
        .OrderBy(static fact => fact.AttributeType, StringComparer.Ordinal)
        .ThenBy(static fact => fact.Display, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The declaration facts produced by every accepted and adversarial inventory surface.</summary>
    public static IReadOnlyList<RecordedAttributeFact> InventoriedValues { get; } = InventoryTypes
        .SelectMany(ReadSurface)
        .DistinctBy(static fact => fact.Display, StringComparer.Ordinal)
        .OrderBy(static fact => fact.AttributeType, StringComparer.Ordinal)
        .ThenBy(static fact => fact.Display, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Reads declared JSON attributes on a type.</summary>
    public static IReadOnlyList<RecordedAttributeFact> ReadType(Type type, string path = "") =>
        Read(type, path);

    /// <summary>Reads declared JSON attributes on a reflected member or constructor.</summary>
    public static IReadOnlyList<RecordedAttributeFact> ReadMember(MemberInfo member, string path) =>
        Read(member, path);

    private static IReadOnlyList<RecordedAttributeFact> ReadSurface(Type type)
    {
        var facts = new List<RecordedAttributeFact>();
        facts.AddRange(ReadType(type, TypeShapes.TypeName(type)));

        const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (ConstructorInfo constructor in type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(static constructor => constructor.ToString(), StringComparer.Ordinal))
        {
            facts.AddRange(ReadMember(constructor, $"{TypeShapes.TypeName(type)} constructor '{constructor}'"));
        }

        foreach (MemberInfo member in type
            .GetMembers(InstanceMembers)
            .Where(static member => member is PropertyInfo or FieldInfo)
            .OrderBy(static member => member.Name, StringComparer.Ordinal))
        {
            facts.AddRange(ReadMember(member, $"{TypeShapes.TypeName(type)} member '{member.Name}'"));
        }

        if (type.IsEnum)
        {
            foreach (FieldInfo field in type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .OrderBy(static field => field.Name, StringComparer.Ordinal))
            {
                facts.AddRange(ReadMember(field, $"{TypeShapes.TypeName(type)} enum member '{field.Name}'"));
            }
        }

        return facts;
    }

    /// <summary>Reads the type and the reflected property/field declarations of one measured surface.</summary>
    public static IReadOnlyList<RecordedAttributeFact> ReadContractSurface(Type type) => ReadSurface(type);

    private static RecordedAttributeFact[] Read(MemberInfo member, string path)
    {
        ArgumentNullException.ThrowIfNull(member);

        return CustomAttributeData.GetCustomAttributes(member)
            .Where(static data =>
                data.AttributeType.Namespace == "System.Text.Json.Serialization" &&
                typeof(Attribute).IsAssignableFrom(data.AttributeType))
            .Select(data => Create(data, path))
            .OrderBy(static fact => fact.AttributeType, StringComparer.Ordinal)
            .ThenBy(static fact => fact.Display, StringComparer.Ordinal)
            .ToArray();
    }

    private static RecordedAttributeFact Create(CustomAttributeData data, string path) =>
        new(
            MetadataSourceKind.DeclaredAttributes,
            path,
            TypeShapes.TypeName(data.AttributeType),
            new ReadOnlyCollection<RecordedAttributeArgument>(
                data.ConstructorArguments
                    .Select((argument, index) => new RecordedAttributeArgument($"${index}", Format(argument)))
                    .Concat(data.NamedArguments
                        .OrderBy(static argument => argument.MemberName, StringComparer.Ordinal)
                        .Select(argument => new RecordedAttributeArgument(argument.MemberName, Format(argument.TypedValue))))
                    .ToArray()));

    private static string Format(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            return $"[{string.Join(",", values.Select(Format))}]";
        }

        if (argument.Value is Type type)
        {
            return TypeShapes.TypeName(type);
        }

        if (argument.ArgumentType.IsEnum && argument.Value is not null)
        {
            return Enum.GetName(argument.ArgumentType, argument.Value) ?? argument.Value.ToString() ?? string.Empty;
        }

        return argument.Value switch
        {
            null => "null",
            string text => text,
            IFormattable value => value.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            _ => argument.Value.ToString() ?? string.Empty,
        };
    }
}

/// <summary>An explicitly excluded runtime System.Text.Json declaration type and its measured reason.</summary>
internal sealed record DeclaredAttributeExclusion(Type Type, string Reason);

/// <summary>One declared JSON attribute and its constructor/named argument values.</summary>
internal sealed record RecordedAttributeFact(
    MetadataSourceKind Source,
    string Path,
    string AttributeType,
    IReadOnlyList<RecordedAttributeArgument> Arguments)
{
    public string Display => Arguments.Count == 0
        ? AttributeType
        : $"{AttributeType}({string.Join(",", Arguments.Select(argument => $"{argument.Name}={DisplayValue(argument)}"))})";

    private string DisplayValue(RecordedAttributeArgument argument) =>
        string.Equals(AttributeType, "System.Text.Json.Serialization.JsonDerivedTypeAttribute", StringComparison.Ordinal) &&
        string.Equals(argument.Name, "$0", StringComparison.Ordinal)
            ? "<derived-type>"
            : argument.Value;
}

/// <summary>One stable name/value pair from a declared attribute.</summary>
internal sealed record RecordedAttributeArgument(string Name, string Value);
