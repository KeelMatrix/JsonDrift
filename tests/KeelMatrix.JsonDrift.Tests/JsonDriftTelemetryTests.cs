using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.Internal;
using Xunit.Abstractions;

namespace KeelMatrix.JsonDrift.Tests;

public sealed partial class JsonDriftTelemetryTests
{
    private static readonly string[] ExpectedClientMethods = ["TrackActivation", "TrackHeartbeat"];
    private static readonly TimeSpan CallerPathBound = TimeSpan.FromMilliseconds(250);
    private readonly ITestOutputHelper output;

    public JsonDriftTelemetryTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void BaselineCreationDoesNotRecordActivation()
    {
        using TemporaryDirectory directory = new();
        RecordingTelemetry telemetry = new();

        using (JsonDriftTelemetryCoordinator.OverrideForTests(telemetry))
        {
            JsonBaseline.Create(TelemetrySourceContext.Default.RootEnvelope, Path.Combine(directory.Path, "baseline.json"), overwrite: false);
        }

        Assert.Equal(0, telemetry.ComparisonCalls);
    }

    [Fact]
    public void ComparisonRequestsExactlyTheSharedActivationAndHeartbeatSignals()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "baseline.json");
        JsonBaseline.Create(TelemetrySourceContext.Default.RootEnvelope, path, overwrite: false);
        CountingClient client = new();
        using JsonDriftTelemetry telemetry = new(() => client, respectProcessOptOut: false);

        JsonDriftReport report;
        using (JsonDriftTelemetryCoordinator.OverrideForTests(telemetry))
        {
            report = JsonDrift.Compare(TelemetrySourceContext.Default.RootEnvelope, path, JsonCompatibility.ReaderBackward);
        }

        Assert.True(report.IsCompatible);
        Assert.True(client.ActivationObserved.Wait(TimeSpan.FromSeconds(1)), "activation was not dispatched");
        Assert.True(client.HeartbeatObserved.Wait(TimeSpan.FromSeconds(1)), "heartbeat was not dispatched");
        Assert.Equal(1, client.ActivationCalls);
        Assert.Equal(1, client.HeartbeatCalls);
    }

    [Fact]
    public void TelemetryClientReceivesNoProductOrIdentityPayload()
    {
        string[] methodNames = typeof(IJsonDriftTelemetryClient)
            .GetMethods()
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .Select(static method => method.Name)
            .ToArray();

        Assert.Equal(ExpectedClientMethods, methodNames);
        Assert.All(
            typeof(IJsonDriftTelemetryClient).GetMethods(),
            static method => Assert.Empty(method.GetParameters()));

        string received = string.Join(",", methodNames);
        Assert.DoesNotContain("https://github.com/KeelMatrix/JsonDrift", received, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFullPath(AppContext.BaseDirectory), received, StringComparison.Ordinal);
        Assert.DoesNotContain("1f492b93eeb1c1c8cb2602edd5b36d8de7127473", received, StringComparison.Ordinal);
        Assert.DoesNotContain("RootEnvelope", received, StringComparison.Ordinal);
        Assert.DoesNotContain("Sensitive", received, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowingTelemetryClientCannotChangeComparisonOutcome()
    {
        JsonContract baseline = JsonDrift.Extract(TelemetrySourceContext.Default.RootEnvelope);
        JsonDriftReport expected = JsonDrift.Compare(
            TelemetrySourceContext.Default.RootEnvelope,
            baseline,
            JsonCompatibility.ReaderBackward);
        ThrowingClient client = new();
        using JsonDriftTelemetry telemetry = new(() => client, respectProcessOptOut: false);

        JsonDriftReport actual;
        using (JsonDriftTelemetryCoordinator.OverrideForTests(telemetry))
        {
            actual = JsonDrift.Compare(
                TelemetrySourceContext.Default.RootEnvelope,
                baseline,
                JsonCompatibility.ReaderBackward);
        }

        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.Changes.Select(static change => change.ToString()), actual.Changes.Select(static change => change.ToString()));
        Assert.True(client.Started.Wait(TimeSpan.FromSeconds(1)), "throwing client was not dispatched");
    }

    [Fact]
    public void BlockingTelemetryClientMatchesNoOpTimingBoundAndBurstUsesOneWorker()
    {
        JsonContract baseline = JsonDrift.Extract(TelemetrySourceContext.Default.RootEnvelope);

        NoOpClient noOpClient = new();
        using JsonDriftTelemetry noOpTelemetry = new(() => noOpClient, respectProcessOptOut: false);
        TimeSpan noOpLatency = MeasureComparison(noOpTelemetry, baseline);

        BlockingClient client = new();
        using JsonDriftTelemetry telemetry = new(() => client, respectProcessOptOut: false);
        TimeSpan blockingLatency = MeasureComparison(telemetry, baseline);

        Assert.True(client.Started.Wait(TimeSpan.FromSeconds(1)), "blocking client was not dispatched");
        Assert.True(
            noOpLatency < CallerPathBound,
            $"no-op comparison caller path took {noOpLatency.TotalMilliseconds:N0} ms, above the documented 250 ms bound");
        Assert.True(
            blockingLatency < CallerPathBound,
            $"blocking comparison caller path took {blockingLatency.TotalMilliseconds:N0} ms, above the documented 250 ms bound; no-op took {noOpLatency.TotalMilliseconds:N0} ms");
        output.WriteLine(
            $"timing evidence: no-op={noOpLatency.TotalMilliseconds:N3} ms, blocking={blockingLatency.TotalMilliseconds:N3} ms, bound={CallerPathBound.TotalMilliseconds:N0} ms");

        for (int index = 0; index < 32; index++)
        {
            telemetry.RecordComparison();
        }

        Assert.Equal(1, telemetry.WorkerTaskCount);
        Assert.Equal(1, client.MaximumConcurrentCalls);
        Assert.Equal(1, client.CallThreadCount);

        client.Release.Set();
    }

    private static TimeSpan MeasureComparison(JsonDriftTelemetry telemetry, JsonContract baseline)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        using (JsonDriftTelemetryCoordinator.OverrideForTests(telemetry))
        {
            JsonDriftReport report = JsonDrift.Compare(
                TelemetrySourceContext.Default.RootEnvelope,
                baseline,
                JsonCompatibility.ReaderBackward);
            Assert.True(report.IsCompatible);
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    [Fact]
    public void ProcessOptOutPreventsSharedClientCalls()
    {
        CountingClient client = new();
        using JsonDriftTelemetry telemetry = new(() => client);

        telemetry.RecordComparison();

        Assert.Equal(0, client.ActivationCalls);
        Assert.Equal(0, client.HeartbeatCalls);
    }

    private sealed class RecordingTelemetry : IJsonDriftTelemetry
    {
        public int ComparisonCalls { get; private set; }

        public void RecordComparison() => ComparisonCalls++;
    }

    private sealed class ThrowingClient : IJsonDriftTelemetryClient
    {
        public ManualResetEventSlim Started { get; } = new();

        public void TrackActivation()
        {
            Started.Set();
            throw new InvalidOperationException("test-only telemetry failure");
        }

        public void TrackHeartbeat() => throw new InvalidOperationException("test-only telemetry failure");
    }

    private sealed class BlockingClient : IJsonDriftTelemetryClient
    {
        private int activeCalls;
        private int maximumConcurrentCalls;
        private readonly HashSet<int> callThreadIds = [];

        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);

        public int CallThreadCount
        {
            get
            {
                lock (callThreadIds)
                {
                    return callThreadIds.Count;
                }
            }
        }

        public void TrackActivation()
        {
            int active = Interlocked.Increment(ref activeCalls);
            UpdateMaximum(active);
            lock (callThreadIds)
            {
                callThreadIds.Add(Environment.CurrentManagedThreadId);
            }
            Started.Set();
            try
            {
                Release.Wait();
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        }

        public void TrackHeartbeat()
        {
        }

        private void UpdateMaximum(int active)
        {
            while (active > Volatile.Read(ref maximumConcurrentCalls))
            {
                if (Interlocked.CompareExchange(ref maximumConcurrentCalls, active, Volatile.Read(ref maximumConcurrentCalls)) == active)
                {
                    return;
                }
            }
        }
    }

    private sealed class NoOpClient : IJsonDriftTelemetryClient
    {
        public void TrackActivation()
        {
        }

        public void TrackHeartbeat()
        {
        }
    }

    private sealed class CountingClient : IJsonDriftTelemetryClient
    {
        private int activationCalls;
        private int heartbeatCalls;

        public ManualResetEventSlim ActivationObserved { get; } = new();

        public ManualResetEventSlim HeartbeatObserved { get; } = new();

        public int ActivationCalls => Volatile.Read(ref activationCalls);

        public int HeartbeatCalls => Volatile.Read(ref heartbeatCalls);

        public void TrackActivation()
        {
            Interlocked.Increment(ref activationCalls);
            ActivationObserved.Set();
        }

        public void TrackHeartbeat()
        {
            Interlocked.Increment(ref heartbeatCalls);
            HeartbeatObserved.Set();
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
