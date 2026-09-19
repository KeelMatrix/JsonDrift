using System.Threading;
using System.Threading.Channels;
using KeelMatrix.Telemetry;

namespace KeelMatrix.JsonDrift.Internal;

internal interface IJsonDriftTelemetryClient
{
    void TrackActivation();

    void TrackHeartbeat();
}

internal interface IJsonDriftTelemetry
{
    void RecordComparison();
}

internal sealed class JsonDriftTelemetry : IJsonDriftTelemetry, IDisposable
{
    private readonly Channel<byte> pendingComparisons = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Func<IJsonDriftTelemetryClient> clientFactory;
    private readonly bool respectProcessOptOut;
    private IJsonDriftTelemetryClient? client;
    private Task? worker;
    private int workerStarted;
    private int workerTaskCount;
    private int disposed;

    public JsonDriftTelemetry()
        : this(() => new SharedTelemetryClient(), respectProcessOptOut: true)
    {
    }

    internal JsonDriftTelemetry(Func<IJsonDriftTelemetryClient> clientFactory, bool respectProcessOptOut = true)
    {
        this.clientFactory = clientFactory;
        this.respectProcessOptOut = respectProcessOptOut;
    }

    public void RecordComparison()
    {
        try
        {
            // KeelMatrix development and CI set the shared process opt-out. The shared
            // client resolves repository-local opt-out before delivery as well.
            if (respectProcessOptOut && TelemetryOptOut.IsProcessDisabled())
            {
                return;
            }

            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            // The comparison caller-path bound is 250 ms on the Windows validation host,
            // including when the client blocks indefinitely. Keep this path non-blocking:
            // TryWrite never waits for the capacity-1 queue and may drop a saturated signal.
            EnsureWorker();
            pendingComparisons.Writer.TryWrite(0);
        }
        catch
        {
            // Telemetry is best-effort and must never affect comparison behavior, including
            // when a custom client or the background dispatch cannot be created.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        pendingComparisons.Writer.TryComplete();
        try
        {
            worker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Test cleanup must not turn a best-effort telemetry failure into a test failure.
        }
    }

    private void EnsureWorker()
    {
        if (Interlocked.CompareExchange(ref workerStarted, 1, 0) != 0)
        {
            return;
        }

        try
        {
            worker = Task.Run(ProcessQueueAsync);
            Interlocked.Increment(ref workerTaskCount);
        }
        catch
        {
            Volatile.Write(ref workerStarted, 0);
        }
    }

    internal int WorkerTaskCount => Volatile.Read(ref workerTaskCount);

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (byte _ in pendingComparisons.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                EmitSharedSignals();
            }
        }
        catch
        {
            // The worker is deliberately isolated from the comparison caller and must never
            // expose a client, queue, or shutdown failure.
        }
    }

    private void EmitSharedSignals()
    {
        try
        {
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

    internal static void RecordComparison()
    {
        try
        {
            (TestOverride.Value ?? Shared).RecordComparison();
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
