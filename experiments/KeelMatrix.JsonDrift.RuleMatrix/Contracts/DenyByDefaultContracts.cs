using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R14 - registered derived types that are collections, dictionaries, or polymorphic bases, and the
// adversarial contracts that try to hide opaque metadata behind a path that is not named by a rule.

/// <summary>An element type a registered collection-derived type carries.</summary>
internal sealed class RevItem
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>An element type whose converter is not framework metadata, reached only through a derived type.</summary>
[JsonConverter(typeof(RevOpaqueItemConverter))]
internal sealed class RevOpaqueItem
{
    public string Value { get; set; } = string.Empty;
}

internal sealed class RevOpaqueItemConverter : JsonConverter<RevOpaqueItem>
{
    public override RevOpaqueItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Value = reader.GetInt32().ToString(CultureInfo.InvariantCulture) };

    public override void Write(Utf8JsonWriter writer, RevOpaqueItem value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value.Length);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RevSequence), "sequence")]
internal abstract class RevSequenceBase
{
}

/// <summary>A registered derived type that is itself a collection.</summary>
internal sealed class RevSequence : RevSequenceBase, ICollection<RevItem>
{
    private readonly List<RevItem> items = new();

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public void Add(RevItem item) => items.Add(item);

    public void Clear() => items.Clear();

    public bool Contains(RevItem item) => items.Contains(item);

    public void CopyTo(RevItem[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<RevItem> GetEnumerator() => items.GetEnumerator();

    public bool Remove(RevItem item) => items.Remove(item);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RevOpaqueSequence), "sequence")]
internal abstract class RevOpaqueSequenceBase
{
}

/// <summary>The same shape with an element type whose converter is not framework metadata.</summary>
internal sealed class RevOpaqueSequence : RevOpaqueSequenceBase, ICollection<RevOpaqueItem>
{
    private readonly List<RevOpaqueItem> items = new();

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public void Add(RevOpaqueItem item) => items.Add(item);

    public void Clear() => items.Clear();

    public bool Contains(RevOpaqueItem item) => items.Contains(item);

    public void CopyTo(RevOpaqueItem[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<RevOpaqueItem> GetEnumerator() => items.GetEnumerator();

    public bool Remove(RevOpaqueItem item) => items.Remove(item);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RevMap), "map")]
internal abstract class RevMapBase
{
}

/// <summary>A registered derived type that is itself a dictionary.</summary>
internal sealed class RevMap : RevMapBase, IDictionary<string, RevItem>
{
    private readonly Dictionary<string, RevItem> items = new(StringComparer.Ordinal);

    public RevItem this[string key]
    {
        get => items[key];
        set => items[key] = value;
    }

    public ICollection<string> Keys => items.Keys;

    public ICollection<RevItem> Values => items.Values;

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public void Add(string key, RevItem value) => items.Add(key, value);

    public void Add(KeyValuePair<string, RevItem> item) => items.Add(item.Key, item.Value);

    public void Clear() => items.Clear();

    public bool Contains(KeyValuePair<string, RevItem> item) =>
        ((ICollection<KeyValuePair<string, RevItem>>)items).Contains(item);

    public bool ContainsKey(string key) => items.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, RevItem>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, RevItem>>)items).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<string, RevItem>> GetEnumerator() => items.GetEnumerator();

    public bool Remove(string key) => items.Remove(key);

    public bool Remove(KeyValuePair<string, RevItem> item) =>
        ((ICollection<KeyValuePair<string, RevItem>>)items).Remove(item);

    public bool TryGetValue(string key, out RevItem value) => items.TryGetValue(key, out value!);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RevOpaqueMap), "map")]
internal abstract class RevOpaqueMapBase
{
}

/// <summary>The same shape with a value type whose converter is not framework metadata.</summary>
internal sealed class RevOpaqueMap : RevOpaqueMapBase, IDictionary<string, RevOpaqueItem>
{
    private readonly Dictionary<string, RevOpaqueItem> items = new(StringComparer.Ordinal);

    public RevOpaqueItem this[string key]
    {
        get => items[key];
        set => items[key] = value;
    }

    public ICollection<string> Keys => items.Keys;

    public ICollection<RevOpaqueItem> Values => items.Values;

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public void Add(string key, RevOpaqueItem value) => items.Add(key, value);

    public void Add(KeyValuePair<string, RevOpaqueItem> item) => items.Add(item.Key, item.Value);

    public void Clear() => items.Clear();

    public bool Contains(KeyValuePair<string, RevOpaqueItem> item) =>
        ((ICollection<KeyValuePair<string, RevOpaqueItem>>)items).Contains(item);

    public bool ContainsKey(string key) => items.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, RevOpaqueItem>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, RevOpaqueItem>>)items).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<string, RevOpaqueItem>> GetEnumerator() => items.GetEnumerator();

    public bool Remove(string key) => items.Remove(key);

    public bool Remove(KeyValuePair<string, RevOpaqueItem> item) =>
        ((ICollection<KeyValuePair<string, RevOpaqueItem>>)items).Remove(item);

    public bool TryGetValue(string key, out RevOpaqueItem value) => items.TryGetValue(key, out value!);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A polymorphic base whose registered derived type is itself a polymorphic base.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyMid), "mid")]
internal abstract class PolyOuterBase
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyOuterLeaf), "leaf")]
internal class PolyMid : PolyOuterBase
{
}

internal sealed class PolyOuterLeaf : PolyMid
{
    public ProbeMoney Amount { get; set; } = new();
}

/// <summary>A polymorphic type reached through a member.</summary>
internal sealed class PolyMemberHolder
{
    public PolyRoot? Node { get; set; }
}

/// <summary>A collection of polymorphic values.</summary>
internal sealed class PolyListHolder
{
    public List<PolyRoot> Nodes { get; set; } = new();
}

/// <summary>A dictionary of polymorphic values.</summary>
internal sealed class PolyMapHolder
{
    public Dictionary<string, PolyRoot> Nodes { get; set; } = new();
}

/// <summary>A registered derived type whose member declares a converter the allowlist does not recognize.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyConverterLeaf), "leaf")]
internal abstract class PolyConverterRoot
{
}

internal sealed class PolyConverterLeaf : PolyConverterRoot
{
    [JsonConverter(typeof(TemperatureConverter))]
    public Temperature Reading { get; set; } = new();
}

/// <summary>A registered derived type whose member declares a framework string-enum converter.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PolyEnumLeaf), "enum")]
internal abstract class PolyEnumRoot
{
}

internal sealed class PolyEnumLeaf : PolyEnumRoot
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderState State { get; set; }
}

/// <summary>A converter factory an application can register for a type.</summary>
internal sealed class AdversarialFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(AdversarialValue);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        new AdversarialValueConverter();
}

internal sealed class AdversarialValue
{
    public decimal Amount { get; set; }
}

internal sealed class AdversarialValueConverter : JsonConverter<AdversarialValue>
{
    public override AdversarialValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new() { Amount = reader.GetString() is string text ? decimal.Parse(text, CultureInfo.InvariantCulture) : 0m };

    public override void Write(Utf8JsonWriter writer, AdversarialValue value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Amount.ToString(CultureInfo.InvariantCulture));
}

internal sealed class AdversarialFactoryHolder
{
    public AdversarialValue Value { get; set; } = new();
}

/// <summary>A contract whose declared type is a generic type argument that carries a converter attribute.</summary>
internal sealed class AdversarialGenericBox<T>
{
    public T? Value { get; set; }
}

internal sealed class AdversarialGenericArgumentHolder
{
    public AdversarialGenericBox<ProbeMoney> Box { get; set; } = new();
}

/// <summary>A member whose scalar type the framework resolves to an unsupported-converter placeholder.</summary>
internal sealed class UnlistedScalarHolder
{
    public Type? Marker { get; set; }
}

/// <summary>
/// A resolver that customizes the metadata of every contract with a type-info modifier, which can replace
/// member converters without leaving a declared marker.
/// </summary>
internal static class AdversarialMetadataCustomization
{
    public static JsonSerializerOptions Options()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static info =>
        {
            foreach (JsonPropertyInfo property in info.Properties)
            {
                if (property.PropertyType == typeof(decimal))
                {
                    property.CustomConverter = new MoneyConverter();
                }
            }
        });

        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    /// <summary>A resolver chain that combines the default reflection resolver with a custom one.</summary>
    public static JsonSerializerOptions Chained()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                new DefaultJsonTypeInfoResolver(),
                new ConverterInjectingResolver()),
        };

        return options;
    }
}
