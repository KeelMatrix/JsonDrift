using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;

namespace KeelMatrix.JsonDrift.Tests;

public sealed partial class JsonDriftFoundationTests
{
    [Fact]
    public void ExtractsFromReflectionOptionsAndRecordsSupportedContract()
    {
        JsonContract contract = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions());

        Assert.True(contract.IsSupported);
        Assert.Null(contract.UnsupportedReason);
        Assert.Equal(1, contract.FormatVersion);
        Assert.Contains("SimpleEnvelope", contract.RootTypeName, StringComparison.Ordinal);
        Assert.Contains("\n", contract.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractsFromSourceGeneratedTypeInfo()
    {
        JsonContract contract = JsonDrift.Extract(SourceContext.Default.SimpleEnvelope);

        Assert.True(contract.IsSupported);
        Assert.Contains("formatVersion", contract.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalBytesAreUtf8WithoutBomAndRepeatable()
    {
        byte[] first = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).GetCanonicalUtf8();
        byte[] second = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).GetCanonicalUtf8();

        Assert.Equal(first, second);
        Assert.False(first.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', first);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.DoesNotContain("\\", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void RecursiveContractTerminatesWithAReferenceRecord()
    {
        JsonContract contract = JsonDrift.Extract<RecursiveNode>(ReflectionOptions());

        Assert.True(contract.IsSupported);
        Assert.Contains("\"kind\": \"reference\"", contract.CanonicalJson, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedConverterIsExplicitAndNamesTheFeature()
    {
        JsonSerializerOptions options = ReflectionOptions();
        SecretConverter converter = new();
        options.Converters.Add(converter);

        JsonContract contract = JsonDrift.Extract<SecretEnvelope>(options);

        Assert.False(contract.IsSupported);
        Assert.Equal(0, converter.InvocationCount);
        Assert.NotNull(contract.UnsupportedReason);
        Assert.Contains("converter", contract.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secret", contract.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BaselineCreateUpdateAndReadAreExplicitAndNonMutatingOnRead()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "simple.json");
        JsonTypeInfo<SimpleEnvelope> typeInfo = (JsonTypeInfo<SimpleEnvelope>)ReflectionOptions().GetTypeInfo(typeof(SimpleEnvelope));

        JsonContract created = JsonBaseline.Create(typeInfo, path, overwrite: false);
        byte[] beforeRead = File.ReadAllBytes(path);
        JsonContract read = JsonBaseline.Read(path);

        Assert.Equal(created.GetCanonicalUtf8(), read.GetCanonicalUtf8());
        Assert.Equal(beforeRead, File.ReadAllBytes(path));
        Assert.Throws<IOException>(() => JsonBaseline.Create(typeInfo, path, overwrite: false));
        InvalidOperationException updateError = Assert.Throws<InvalidOperationException>(() => JsonBaseline.Update(typeInfo, path, overwrite: false));
        Assert.Equal("Baseline update requires overwrite: true.", updateError.Message);
        Assert.Throws<IOException>(() => JsonBaseline.Create(typeInfo, path, overwrite: false));
        JsonBaseline.Update(typeInfo, path, overwrite: true);
    }

    [Fact]
    public void ReadRejectsMalformedForeignOldFutureDepthAndOversizedDocuments()
    {
        using TemporaryDirectory directory = new();

        AssertReadFailure(directory, "malformed.json", "not-json\n", "Baseline is malformed JSON.");
        AssertReadFailure(directory, "foreign.json", "{\"name\":\"other\"}\n", "Baseline is not a JsonDrift canonical contract: missing formatVersion.");
        AssertReadFailure(directory, "old.json", "{\"formatVersion\":0}\n", "Baseline format version 0 is not supported.");
        AssertReadFailure(directory, "future.json", "{\"formatVersion\":2}\n", "Baseline format version 2 is newer than supported version 1.");

        string deep = new string('[', 12) + new string(']', 12) + "\n";
        string depthPath = Path.Combine(directory.Path, "depth.json");
        File.WriteAllText(depthPath, deep, new UTF8Encoding(false));
        InvalidDataException depthError = Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(depthPath, new JsonBaselineLimits(maximumDepth: 3)));
        Assert.Equal("Baseline exceeds the maximum nesting depth of 3.", depthError.Message);

        string sizePath = Path.Combine(directory.Path, "size.json");
        File.WriteAllText(sizePath, "{}\n", new UTF8Encoding(false));
        InvalidDataException sizeError = Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(sizePath, new JsonBaselineLimits(maximumBytes: 2)));
        Assert.Equal("Baseline exceeds the maximum size of 2 bytes.", sizeError.Message);
    }

    [Fact]
    public void ReadRejectsTruncatedIncompleteUnknownDuplicateAndInconsistentDocuments()
    {
        using TemporaryDirectory directory = new();
        string canonical = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson;

        string truncatedPath = Path.Combine(directory.Path, "truncated.json");
        File.WriteAllText(truncatedPath, canonical[..^2] + "\n", new UTF8Encoding(false));
        Assert.IsType<InvalidDataException>(Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(truncatedPath)));

        AssertReadFailure(
            directory,
            "missing-field.json",
            canonical.Replace("    \"path\": \"root\",\n", string.Empty, StringComparison.Ordinal),
            "Baseline root is missing required property 'path'.");

        AssertReadFailure(
            directory,
            "unknown-field.json",
            canonical.Replace("  \"overallSupported\": true\n", "  \"unknown\": true,\n  \"overallSupported\": true\n", StringComparison.Ordinal),
            "Baseline top-level contains an unknown property.");

        AssertReadFailure(
            directory,
            "duplicate-member.json",
            canonical.Replace("  \"formatVersion\": 1,\n", "  \"formatVersion\": 1,\n  \"formatVersion\": 1,\n", StringComparison.Ordinal),
            "Baseline contains duplicate JSON members.");

        AssertReadFailure(
            directory,
            "false-overall-support.json",
            canonical.Replace("  \"overallSupported\": true\n", "  \"overallSupported\": false\n", StringComparison.Ordinal),
            "Baseline has inconsistent overallSupported and root support state.");

        AssertReadFailure(
            directory,
            "supported-with-reason.json",
            canonical.Replace("    \"supported\": true,\n    \"declaredAttributes\"", "    \"supported\": true,\n    \"reason\": \"forged\",\n    \"declaredAttributes\"", StringComparison.Ordinal),
            "Baseline root has inconsistent support and reason state.");
    }

    [Fact]
    public void ReadAcceptsDocumentExactlyAtTheConfiguredByteLimitAndRejectsOneByteOver()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "bounded.json");
        byte[] canonical = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).GetCanonicalUtf8();
        File.WriteAllBytes(path, canonical);

        JsonContract read = JsonBaseline.Read(path, new JsonBaselineLimits(maximumBytes: canonical.Length));
        Assert.Equal(canonical, read.GetCanonicalUtf8());

        File.WriteAllBytes(path, canonical.Concat(new byte[] { (byte)' ' }).ToArray());
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            JsonBaseline.Read(path, new JsonBaselineLimits(maximumBytes: canonical.Length)));
        Assert.Equal($"Baseline exceeds the maximum size of {canonical.Length:N0} bytes.", error.Message);
    }

    [Fact]
    public async Task ConcurrentNonOverwritingCreatesHaveExactlyOneWinner()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "race.json");
        JsonTypeInfo<SimpleEnvelope> typeInfo = (JsonTypeInfo<SimpleEnvelope>)ReflectionOptions().GetTypeInfo(typeof(SimpleEnvelope));

        Task<bool>[] attempts = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    JsonBaseline.Create(typeInfo, path, overwrite: false);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }))
            .ToArray();

        bool[] winners = await Task.WhenAll(attempts);

        Assert.Equal(1, winners.Count(static winner => winner));
        Assert.True(File.Exists(path));
        Assert.Equal(JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson, JsonBaseline.Read(path).CanonicalJson);
    }

    [Fact]
    public async Task UpdateThatLosesAConcurrentDeleteFailsWithoutCreatingABaseline()
    {
        using TemporaryDirectory directory = new();
        JsonTypeInfo<SimpleEnvelope> typeInfo = (JsonTypeInfo<SimpleEnvelope>)ReflectionOptions().GetTypeInfo(typeof(SimpleEnvelope));
        bool observedDeleteWinner = false;

        for (int attempt = 0; attempt < 64 && !observedDeleteWinner; attempt++)
        {
            string path = Path.Combine(directory.Path, $"update-race-{attempt}.json");
            JsonBaseline.Create(typeInfo, path, overwrite: false);
            using var start = new ManualResetEventSlim(false);

            Task<Exception?> update = Task.Run(() =>
            {
                start.Wait();
                try
                {
                    JsonBaseline.Update(typeInfo, path, overwrite: true);
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            Task delete = Task.Run(() =>
            {
                start.Wait();
                File.Delete(path);
            });

            start.Set();
            Exception? updateError = await update;
            await delete;

            if (updateError is IOException)
            {
                observedDeleteWinner = true;
                Assert.False(File.Exists(path));
                Assert.Contains("atomically update baseline", updateError.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Null(updateError);
                File.Delete(path);
            }
        }

        Assert.True(observedDeleteWinner, "The race did not exercise the concurrent-delete loser path.");
    }

    [Fact]
    public void ReadRejectsBomAndCrlfBaselines()
    {
        using TemporaryDirectory directory = new();
        string bomPath = Path.Combine(directory.Path, "bom.json");
        File.WriteAllBytes(bomPath, new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes("{}\n")).ToArray());
        Assert.Equal("Baseline must be UTF-8 without a byte order mark.", Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(bomPath)).Message);

        string crlfPath = Path.Combine(directory.Path, "crlf.json");
        File.WriteAllText(crlfPath, "{}\r\n", new UTF8Encoding(false));
        Assert.Equal("Baseline must use LF line endings.", Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(crlfPath)).Message);
    }

    [Fact]
    public void ExtractionIsSafeUnderConcurrency()
    {
        string expected = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson;
        string[] results = new string[32];

        Parallel.For(0, results.Length, index => results[index] = JsonDrift.Extract<SimpleEnvelope>(ReflectionOptions()).CanonicalJson);

        Assert.All(results, result => Assert.Equal(expected, result));
    }

    [Fact]
    public void PublicTypesAndMembersHaveXmlDocumentation()
    {
        string xmlPath = Path.ChangeExtension(typeof(JsonDrift).Assembly.Location, ".xml");
        Assert.True(File.Exists(xmlPath), $"Missing XML documentation file: {xmlPath}");
        XDocument xml = XDocument.Load(xmlPath);
        HashSet<string> documented = xml.Descendants("member")
            .Select(static element => (string?)element.Attribute("name"))
            .Where(static name => name is not null)
            .Select(static name => name!)
            .ToHashSet(StringComparer.Ordinal);

        Type[] publicTypes = typeof(JsonDrift).Assembly.GetExportedTypes()
            .Where(static type => type.Namespace == "KeelMatrix.JsonDrift")
            .ToArray();
        Assert.NotEmpty(publicTypes);
        foreach (Type type in publicTypes)
        {
            Assert.Contains($"T:{type.FullName}", documented);
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (member is MethodInfo { IsSpecialName: true } ||
                    member is not (MethodInfo or PropertyInfo or ConstructorInfo or FieldInfo) ||
                    member.Name == "value__")
                {
                    continue;
                }

                string prefix = member switch
                {
                    ConstructorInfo => "M:",
                    MethodInfo => "M:",
                    PropertyInfo => "P:",
                    FieldInfo => "F:",
                    _ => string.Empty,
                };
                string memberName = member is ConstructorInfo ? "#ctor" : member.Name;
                Assert.Contains(documented, name => name.StartsWith(prefix + type.FullName + "." + memberName, StringComparison.Ordinal));
            }
        }
    }

    private static JsonSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static void AssertReadFailure(TemporaryDirectory directory, string fileName, string content, string message)
    {
        string path = Path.Combine(directory.Path, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => JsonBaseline.Read(path));
        Assert.Equal(message, error.Message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jsondrift-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class Secret
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class SecretEnvelope
    {
        public Secret Data { get; set; } = new();
    }

    private sealed class SecretConverter : JsonConverter<Secret>
    {
        public int InvocationCount { get; private set; }

        public override Secret? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            InvocationCount++;
            return new() { Value = reader.GetString() ?? string.Empty };
        }

        public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options)
        {
            InvocationCount++;
            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class SimpleEnvelope
    {
        public int Id { get; set; }

        public string? Note { get; set; }
    }

    private sealed class RecursiveNode
    {
        public string? Value { get; set; }

        public RecursiveNode? Next { get; set; }
    }

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(SimpleEnvelope))]
    private sealed partial class SourceContext : JsonSerializerContext
    {
    }
}
