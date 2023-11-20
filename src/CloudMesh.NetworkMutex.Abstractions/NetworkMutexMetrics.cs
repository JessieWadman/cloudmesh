using System.Diagnostics.Metrics;

namespace CloudMesh.NetworkMutex.Abstractions;

public static class NetworkMutexMetrics
{
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