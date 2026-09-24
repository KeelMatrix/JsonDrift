using System.Reflection;
using System.Text.Json;
using KeelMatrix.JsonDrift.Internal;

#pragma warning disable SYSLIB0020

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// The reviewed disposition of one public <see cref="JsonSerializerOptions"/> property. The runtime property
/// surface is always the outer set: this declaration list may classify it, but it may not define which
/// properties exist.
/// </summary>
internal sealed record SerializerOptionDeclaration(string PropertyName, string Disposition, string Evidence);

/// <summary>The validation result for the runtime serializer-option declaration inventory.</summary>
internal sealed record SerializerOptionInventoryValidation(
    IReadOnlyList<string> RuntimeWithoutDisposition,
    IReadOnlyList<string> DispositionOutsideRuntime,
    IReadOnlyList<string> DuplicateDeclarations,
    IReadOnlyList<string> UnknownDispositions,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> RecordedWithoutFact,
    IReadOnlyList<string> FactsWithoutRecordedDisposition)
{
    public bool Passed => RuntimeWithoutDisposition.Count == 0 &&
        DispositionOutsideRuntime.Count == 0 &&
        DuplicateDeclarations.Count == 0 &&
        UnknownDispositions.Count == 0 &&
        MissingEvidence.Count == 0 &&
        RecordedWithoutFact.Count == 0 &&
        FactsWithoutRecordedDisposition.Count == 0;
}

/// <summary>
/// Enumerates and validates the exact public option surface from the loaded System.Text.Json assembly.
/// Adding a framework property without a reviewed disposition fails the matrix before the option allowlist
/// is consulted.
/// </summary>
internal static class SerializerOptionInventory
{
    public const string RecordedAndCompared = "RecordedAndCompared";
    public const string ProjectedIntoEffectiveContract = "ProjectedIntoEffectiveContract";
    public const string NonContractBearing = "NonContractBearing";
    public const string UnsupportedWhenNonDefault = "UnsupportedWhenNonDefault";

    private static readonly string[] KnownDispositions =
    {
        RecordedAndCompared,
        ProjectedIntoEffectiveContract,
        NonContractBearing,
        UnsupportedWhenNonDefault,
    };

    /// <summary>The public, parameterless instance properties in the loaded System.Text.Json assembly.</summary>
    public static IReadOnlyList<PropertyInfo> RuntimeProperties { get; } = typeof(JsonSerializerOptions)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(static property => property.GetIndexParameters().Length == 0 && property.GetMethod?.IsPublic == true)
        .OrderBy(static property => property.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The reviewed dispositions for the current runtime surface.</summary>
    public static IReadOnlyList<SerializerOptionDeclaration> Declarations { get; } = new[]
    {
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.AllowDuplicateProperties),
            UnsupportedWhenNonDefault,
            "Recorded as a serializer option; false is explicitly unsupported because duplicate-name reader behavior is not measured."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.AllowOutOfOrderMetadataProperties),
            UnsupportedWhenNonDefault,
            "Recorded as a serializer option; true is explicitly unsupported because polymorphic metadata ordering is not measured."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.AllowTrailingCommas),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted value fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.Converters),
            ProjectedIntoEffectiveContract,
            "Registered converters are recorded by the options-converters discovery source and unrecognized converters fail closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.DefaultBufferSize),
            NonContractBearing,
            "Only changes serializer buffering; it does not change JSON tokens, member shape, or ReaderBackward losslessness."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.DefaultIgnoreCondition),
            RecordedAndCompared,
            "Recorded and compared; the matrix proves the measured null-writing transition."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.DictionaryKeyPolicy),
            RecordedAndCompared,
            "Recorded and compared; the dictionary-key policy changes wire member names and unallowlisted values fail closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.Encoder),
            NonContractBearing,
            "Only changes escaping of equivalent JSON string values; ReaderBackward compares parsed wire values, not byte escaping."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IgnoreNullValues),
            UnsupportedWhenNonDefault,
            "Recorded as a serializer option; true is explicitly unsupported, with a public regression proving null-valued member writing and reading differ."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IgnoreReadOnlyFields),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted value fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IgnoreReadOnlyProperties),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted value fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IncludeFields),
            UnsupportedWhenNonDefault,
            "Recorded as a serializer option; true is explicitly unsupported because enabling fields changes the effective member surface."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IndentCharacter),
            NonContractBearing,
            "Only changes indentation formatting; it does not change JSON tokens or ReaderBackward losslessness."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IndentSize),
            NonContractBearing,
            "Only changes indentation formatting; it does not change JSON tokens or ReaderBackward losslessness."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.IsReadOnly),
            NonContractBearing,
            "Reports whether options can still be mutated; the state itself does not change the effective JSON contract."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.MaxDepth),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted depth fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.NewLine),
            NonContractBearing,
            "Only changes line-ending formatting; it does not change JSON tokens or ReaderBackward losslessness."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.NumberHandling),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves unallowlisted token handling fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.PreferredObjectCreationHandling),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted materialization value fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.PropertyNameCaseInsensitive),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted reader matching value fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.PropertyNamingPolicy),
            RecordedAndCompared,
            "Recorded and compared; the matrix measures serialized-name changes under the naming policy."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.ReadCommentHandling),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted comment value fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.ReferenceHandler),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted reference handler fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.RespectNullableAnnotations),
            RecordedAndCompared,
            "Recorded and compared; the matrix measures the enforced nullable-reader transition."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.RespectRequiredConstructorParameters),
            RecordedAndCompared,
            "Recorded and compared; the matrix measures the enforced constructor-presence transition."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.TypeInfoResolver),
            ProjectedIntoEffectiveContract,
            "The resolver identity, chain length, and modifiers are recorded as resolver facts; unrecognized changes fail closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.TypeInfoResolverChain),
            ProjectedIntoEffectiveContract,
            "The resolver chain is recorded as resolver facts; multiple or unrecognized resolvers fail closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.UnknownTypeHandling),
            UnsupportedWhenNonDefault,
            "Recorded as a serializer option; JsonNode is explicitly unsupported because its object materialization is not measured."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.UnmappedMemberHandling),
            RecordedAndCompared,
            "Recorded and compared; the options matrix proves an unallowlisted reader behavior fails closed."),
        new SerializerOptionDeclaration(
            nameof(JsonSerializerOptions.WriteIndented),
            NonContractBearing,
            "Only changes whitespace formatting; it does not change JSON tokens or ReaderBackward losslessness."),
    };

    public static IReadOnlyList<string> RuntimePropertyNames { get; } = RuntimeProperties
        .Select(static property => property.Name)
        .ToArray();

    public static SerializerOptionInventoryValidation Validate(
        IEnumerable<string>? runtimePropertyNames = null,
        IEnumerable<SerializerOptionDeclaration>? declarations = null)
    {
        string[] runtime = (runtimePropertyNames ?? RuntimePropertyNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        SerializerOptionDeclaration[] declared = (declarations ?? Declarations).ToArray();
        string[] declaredNames = declared.Select(static declaration => declaration.PropertyName).ToArray();
        HashSet<string> recordedProperties = SerializerOptionFacts.All
            .Select(SerializerOptionFacts.PropertyName)
            .ToHashSet(StringComparer.Ordinal);
        static bool IsRecordedDisposition(string disposition) =>
            string.Equals(disposition, RecordedAndCompared, StringComparison.Ordinal) ||
            string.Equals(disposition, UnsupportedWhenNonDefault, StringComparison.Ordinal);

        return new SerializerOptionInventoryValidation(
            runtime.Where(name => !declaredNames.Contains(name, StringComparer.Ordinal)).ToArray(),
            declaredNames.Where(name => !runtime.Contains(name, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            declaredNames.GroupBy(static name => name, StringComparer.Ordinal).Where(static group => group.Count() != 1).Select(static group => group.Key).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            declared.Where(declaration => !KnownDispositions.Contains(declaration.Disposition, StringComparer.Ordinal)).Select(static declaration => $"{declaration.PropertyName}={declaration.Disposition}").OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            declared.Where(static declaration => string.IsNullOrWhiteSpace(declaration.Evidence)).Select(static declaration => declaration.PropertyName).ToArray(),
            declared.Where(declaration => IsRecordedDisposition(declaration.Disposition) && !recordedProperties.Contains(declaration.PropertyName)).Select(static declaration => declaration.PropertyName).ToArray(),
            recordedProperties.Where(name => !declared.Any(declaration => string.Equals(declaration.PropertyName, name, StringComparison.Ordinal) && IsRecordedDisposition(declaration.Disposition))).OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }
}

#pragma warning restore SYSLIB0020
