using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System;

// The (stopwatch tick, wall-clock ms) pair that stopwatch-based projections are measured from. Immutable,
// so it can be published atomically with a single reference swap — no lock needed.
internal sealed class ClockOrigin(long stopwatchTicks, long unixMilliseconds)
{
    /// <summary>The origin captured once, at process start. Shared by the monotonic and fast clocks.</summary>
    public static readonly ClockOrigin Startup = new(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public readonly long StopwatchTicks = stopwatchTicks;
    public readonly long UnixMilliseconds = unixMilliseconds;

    /// <summary>Projects a raw stopwatch reading to Unix milliseconds relative to this origin.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToUnixMilliseconds(long stopwatchTicks)
        => UnixMilliseconds + ClockMath.StopwatchTicksToMilliseconds(stopwatchTicks - StopwatchTicks);
}
