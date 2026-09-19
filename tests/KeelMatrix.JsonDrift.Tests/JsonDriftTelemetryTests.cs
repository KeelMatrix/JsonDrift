using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.Internal;

namespace KeelMatrix.JsonDrift.Tests;

public sealed partial class JsonDriftTelemetryTests
{
    private static readonly string[] AllowedPayloadFields =
    [
        "PackageVersion",
        "TargetFramework",
        "CompatibilityMode",
        "RootContractCount",
        "Outcome",
        "SourceGeneratedMetadataUsed",
    ];

    [Fact]
    public void BaselineCreationDoesNotRecordActivation()
    {
        using TemporaryDirectory directory = new();
        RecordingTelemetry telemetry = new();

        using (JsonDriftTelemetryCoordinator.OverrideForTests(telemetry))
        {
            JsonBaseline.Create(TelemetrySourceContext.Default.RootEnvelope, Path.Combine(directory.Path, "baseline.json"), overwrite: false);
        }

        Assert.Empty(telemetry.Payloads);
    }

    [Fact]
    public void ComparisonEmitsOnlyTheAllowlistedAggregatePayload()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "baseline.json");
        JsonBaseline.Create(TelemetrySourceContext.Default.RootEnvelope, path, overwrite: false);
        RecordingTelemetry telemetry = new();

        JsonDriftReport report;
        using (JsonDriftTelemetryCoordinator.OverrideForTests(telemetry))
        {
            report = JsonDrift.Compare(TelemetrySourceContext.Default.RootEnvelope, path, JsonCompatibility.ReaderBackward);
        }

        Assert.True(report.IsCompatible);
        JsonDriftTelemetryPayload payload = Assert.Single(telemetry.Payloads);
        Assert.Equal("0.1.0", payload.PackageVersion);
        Assert.Equal("net8.0", payload.TargetFramework);
        Assert.Equal("ReaderBackward", payload.CompatibilityMode);
        Assert.Equal(1, payload.RootContractCount);
        Assert.Equal("Compatible", payload.Outcome);
        Assert.True(payload.SourceGeneratedMetadataUsed);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        string[] fields = document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray();
        Assert.Equal(
            AllowedPayloadFields,
            fields);

        string serialized = document.RootElement.GetRawText();
        Assert.DoesNotContain("JsonDriftTelemetryTests", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("KeelMatrix.JsonDrift.Tests", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RootEnvelope", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowingOrDelayedTelemetryCannotChangeComparisonOutcome()
    {
        JsonContract baseline = JsonDrift.Extract(TelemetrySourceContext.Default.RootEnvelope);

        JsonDriftReport expected = JsonDrift.Compare(
            TelemetrySourceContext.Default.RootEnvelope,
            baseline,
            JsonCompatibility.ReaderBackward);

        JsonDriftTelemetry throwingTelemetry = new(() => new ThrowingClient());
        using (JsonDriftTelemetryCoordinator.OverrideForTests(throwingTelemetry))
        {
            JsonDriftReport actual = JsonDrift.Compare(
                TelemetrySourceContext.Default.RootEnvelope,
                baseline,
                JsonCompatibility.ReaderBackward);

            Assert.Equal(expected.Outcome, actual.Outcome);
            Assert.Equal(expected.Changes.Select(static change => change.ToString()), actual.Changes.Select(static change => change.ToString()));
        }

        JsonDriftTelemetry delayedTelemetry = new(() => new DelayedClient());
        using (JsonDriftTelemetryCoordinator.OverrideForTests(delayedTelemetry))
        {
            JsonDriftReport actual = JsonDrift.Compare(
                TelemetrySourceContext.Default.RootEnvelope,
                baseline,
                JsonCompatibility.ReaderBackward);

            Assert.Equal(expected.Outcome, actual.Outcome);
            Assert.Equal(expected.Changes.Select(static change => change.ToString()), actual.Changes.Select(static change => change.ToString()));
        }
    }

    [Fact]
    public void ProcessOptOutPreventsSharedClientCalls()
    {
        CountingClient client = new();
        JsonDriftTelemetry telemetry = new(() => client);

        telemetry.RecordComparison(new JsonDriftTelemetryPayload(
            "0.1.0",
            "net8.0",
            "ReaderBackward",
            1,
            "Compatible",
            false));

        Assert.Equal(0, client.ActivationCalls);
        Assert.Equal(0, client.HeartbeatCalls);
    }

    private sealed class RecordingTelemetry : IJsonDriftTelemetry
    {
        public List<JsonDriftTelemetryPayload> Payloads { get; } = new();

        public void RecordComparison(JsonDriftTelemetryPayload payload) => Payloads.Add(payload);
    }

    private sealed class ThrowingClient : IJsonDriftTelemetryClient
    {
        public void TrackActivation() => throw new InvalidOperationException("test-only telemetry failure");

        public void TrackHeartbeat() => throw new InvalidOperationException("test-only telemetry failure");
    }

    private sealed class DelayedClient : IJsonDriftTelemetryClient
    {
        public void TrackActivation() => Thread.Sleep(25);

        public void TrackHeartbeat() => Thread.Sleep(25);
    }

    private sealed class CountingClient : IJsonDriftTelemetryClient
    {
        public int ActivationCalls { get; private set; }

        public int HeartbeatCalls { get; private set; }

        public void TrackActivation() => ActivationCalls++;

        public void TrackHeartbeat() => HeartbeatCalls++;
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
