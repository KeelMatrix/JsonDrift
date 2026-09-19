using System.Threading;
using KeelMatrix.Telemetry;

namespace KeelMatrix.JsonDrift.Internal;

internal interface IJsonDriftTelemetryClient
{
    void TrackActivation();

    void TrackHeartbeat();
}

internal interface IJsonDriftTelemetry
{
    void RecordComparison(JsonDriftTelemetryPayload payload);
}

internal sealed record JsonDriftTelemetryPayload(
    string PackageVersion,
    string TargetFramework,
    string CompatibilityMode,
    int RootContractCount,
    string Outcome,
    bool SourceGeneratedMetadataUsed);

internal sealed class JsonDriftTelemetry : IJsonDriftTelemetry
{
    private readonly Func<IJsonDriftTelemetryClient> clientFactory;
    private IJsonDriftTelemetryClient? client;

    public JsonDriftTelemetry()
        : this(() => new SharedTelemetryClient())
    {
    }

    internal JsonDriftTelemetry(Func<IJsonDriftTelemetryClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public void RecordComparison(JsonDriftTelemetryPayload payload)
    {
        try
        {
            // KeelMatrix development and CI set the shared process opt-out. The shared
            // client resolves repository-local opt-out before delivery as well.
            if (TelemetryOptOut.IsProcessDisabled())
            {
                return;
            }

            client ??= clientFactory();
            client.TrackActivation();
            client.TrackHeartbeat();
        }
        catch
        {
            // Telemetry is best-effort and must never affect comparison behavior.
        }
    }

    private sealed class SharedTelemetryClient : IJsonDriftTelemetryClient
    {
        private readonly Client client = new("jsondrift", typeof(JsonDrift));

        public void TrackActivation() => client.TrackActivation();

        public void TrackHeartbeat() => client.TrackHeartbeat();
    }
}

internal static class JsonDriftTelemetryCoordinator
{
    private static readonly AsyncLocal<IJsonDriftTelemetry?> TestOverride = new();
    private static readonly IJsonDriftTelemetry Shared = new JsonDriftTelemetry();

    internal static void RecordComparison(
        JsonContract contract,
        JsonCompatibility compatibility,
        JsonDriftReport report)
    {
        JsonDriftTelemetryPayload payload = new(
            PackageVersion: typeof(JsonDrift).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            TargetFramework: "net8.0",
            CompatibilityMode: compatibility.ToString(),
            RootContractCount: 1,
            Outcome: report.Outcome.ToString(),
            SourceGeneratedMetadataUsed: contract.UsesSourceGeneratedMetadata);

        try
        {
            (TestOverride.Value ?? Shared).RecordComparison(payload);
        }
        catch
        {
            // A custom/test reporter has the same failure-isolation guarantee.
        }
    }

    internal static IDisposable OverrideForTests(IJsonDriftTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        IJsonDriftTelemetry? previous = TestOverride.Value;
        TestOverride.Value = telemetry;
        return new RestoreOverride(previous);
    }

    private sealed class RestoreOverride : IDisposable
    {
        private readonly IJsonDriftTelemetry? previous;

        public RestoreOverride(IJsonDriftTelemetry? previous)
        {
            this.previous = previous;
        }

        public void Dispose() => TestOverride.Value = previous;
    }
}

internal static class TelemetryOptOut
{
    private static readonly string[] EnvironmentKeys =
    [
        "KEELMATRIX_NO_TELEMETRY",
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "DO_NOT_TRACK"
    ];

    internal static bool IsProcessDisabled()
    {
        try
        {
            return EnvironmentKeys.Any(key => IsTruthy(Environment.GetEnvironmentVariable(key)));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTruthy(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "y" or "on";
}
