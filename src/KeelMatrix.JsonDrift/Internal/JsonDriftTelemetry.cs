using System.Threading;
using KeelMatrix.Telemetry;

namespace KeelMatrix.JsonDrift.Internal;

internal interface IJsonDriftTelemetryClient
{
    void TrackActivation();

    void TrackHeartbeat();
}

internal static class JsonDriftTelemetryCoordinator
{
    private static readonly AsyncLocal<IJsonDriftTelemetryClient?> TestOverride = new();
    private static readonly IJsonDriftTelemetryClient Shared = new SharedTelemetryClient();

    internal static void RecordComparison()
    {
        IJsonDriftTelemetryClient client = TestOverride.Value ?? Shared;
        client.TrackActivation();
        client.TrackHeartbeat();
    }

    internal static IDisposable OverrideForTests(IJsonDriftTelemetryClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        IJsonDriftTelemetryClient? previous = TestOverride.Value;
        TestOverride.Value = client;
        return new RestoreOverride(previous);
    }

    private sealed class SharedTelemetryClient : IJsonDriftTelemetryClient
    {
        private readonly Client client = new("jsondrift", typeof(JsonDrift));

        public void TrackActivation() => client.TrackActivation();

        public void TrackHeartbeat() => client.TrackHeartbeat();
    }

    private sealed class RestoreOverride : IDisposable
    {
        private readonly IJsonDriftTelemetryClient? previous;

        public RestoreOverride(IJsonDriftTelemetryClient? previous)
        {
            this.previous = previous;
        }

        public void Dispose() => TestOverride.Value = previous;
    }
}
