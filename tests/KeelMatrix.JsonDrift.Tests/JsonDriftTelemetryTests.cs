using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.Internal;

namespace KeelMatrix.JsonDrift.Tests;

public sealed partial class JsonDriftTelemetryTests
{
    private static readonly string[] ExpectedTelemetryMethods = ["TrackActivation", "TrackHeartbeat"];

    [Fact]
    public void BaselineCreationDoesNotRecordActivation()
    {
        using TemporaryDirectory directory = new();
        RecordingClient client = new();

        using (JsonDriftTelemetryCoordinator.OverrideForTests(client))
        {
            JsonBaseline.Create(
                TelemetrySourceContext.Default.RootEnvelope,
                Path.Combine(directory.Path, "baseline.json"),
                overwrite: false);
        }

        Assert.Equal(0, client.ActivationCalls);
        Assert.Equal(0, client.HeartbeatCalls);
    }

    [Fact]
    public void RealComparisonRequestsActivationAndHeartbeatWithoutProductPayload()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "baseline.json");
        JsonBaseline.Create(TelemetrySourceContext.Default.RootEnvelope, path, overwrite: false);
        RecordingClient client = new();

        JsonDriftReport report;
        using (JsonDriftTelemetryCoordinator.OverrideForTests(client))
        {
            report = JsonDrift.Compare(TelemetrySourceContext.Default.RootEnvelope, path, JsonCompatibility.ReaderBackward);
        }

        Assert.True(report.IsCompatible);
        Assert.Equal(1, client.ActivationCalls);
        Assert.Equal(1, client.HeartbeatCalls);
        Assert.Equal(ExpectedTelemetryMethods, client.Methods);
    }

    [Fact]
    public void TelemetryTestSeamAcceptsNoProductOrIdentityPayload()
    {
        string[] methodNames = typeof(IJsonDriftTelemetryClient)
            .GetMethods()
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .Select(static method => method.Name)
            .ToArray();

        Assert.Equal(ExpectedTelemetryMethods, methodNames);
        Assert.All(
            typeof(IJsonDriftTelemetryClient).GetMethods(),
            static method => Assert.Empty(method.GetParameters()));
    }

    private sealed class RecordingClient : IJsonDriftTelemetryClient
    {
        private readonly List<string> methods = [];

        public int ActivationCalls { get; private set; }

        public int HeartbeatCalls { get; private set; }

        public IReadOnlyList<string> Methods => methods;

        public void TrackActivation()
        {
            ActivationCalls++;
            methods.Add(nameof(TrackActivation));
        }

        public void TrackHeartbeat()
        {
            HeartbeatCalls++;
            methods.Add(nameof(TrackHeartbeat));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jsondrift-telemetry-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class RootEnvelope
    {
        public string Id { get; set; } = string.Empty;

        public string? Sensitive { get; set; }
    }

    [JsonSerializable(typeof(RootEnvelope))]
    private sealed partial class TelemetrySourceContext : JsonSerializerContext
    {
    }
}
