using System.Diagnostics.Metrics;

namespace CloudMesh.NetworkMutex.Abstractions;

/// <summary>
/// The <see cref="System.Diagnostics.Metrics"/> instrumentation emitted by the network-mutex implementations.
/// Subscribe to the meter named <see cref="MeterName"/> (for example via OpenTelemetry) to observe lock
/// acquisition, wait times, hold durations, timeouts and errors.
/// </summary>
public static class NetworkMutexMetrics
{
    /// <summary>
    /// The name of the <see cref="Meter"/> under which all network-mutex metrics are published. Register this
    /// name with your metrics pipeline to collect the counters and histograms below.
    /// </summary>
    // Need to make this public, so it can be read by consumers of the library
    public const string MeterName = "mutex.network";

    private static readonly Meter Meter = new(MeterName);
    internal static readonly Counter<int> Timeouts = Meter.CreateCounter<int>("network_mutex.timeouts", "Count");
    internal static readonly Counter<int> Errors = Meter.CreateCounter<int>("network_mutex.errors", "Count");
#if (NET8_0_OR_GREATER)
    internal static readonly UpDownCounter<int> Locks = Meter.CreateUpDownCounter<int>("network_mutex.lock_count", "Lock");
#endif
    internal static readonly Histogram<long> LockWaitTime = Meter.CreateHistogram<long>("network_mutex.wait_time", "ms");
    internal static readonly Histogram<long> LockDuration = Meter.CreateHistogram<long>("network_mutex.lock_duration", "ms");
}
