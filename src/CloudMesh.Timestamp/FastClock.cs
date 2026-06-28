using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System;

/// <summary>
/// A fast, wall-clock-tracking clock with a millisecond-facing API. Like <see cref="HighResolutionTimestamp"/>
/// it reads <see cref="Stopwatch"/>, but it periodically re-anchors its origin to the system clock (by default
/// at most once every 5 seconds) so its projections catch up to NTP corrections and VM suspend/resume instead
/// of drifting. Re-anchoring is lock-free and amortized — only the first call after the interval elapses pays
/// for it. Use this when you want a cheap, always-accurate wall-clock reading.
/// </summary>
public static class FastClock
{
    private static ClockOrigin _origin = ClockOrigin.Startup;
    private static long _lastAdjustmentTicks = ClockOrigin.Startup.StopwatchTicks;
    private static long _adjustIntervalTicks = ClockMath.StopwatchFrequency * 5; // ~5 seconds

    /// <summary>
    /// How often the clock may re-anchor to the system clock. Defaults to 5 seconds. The cost is amortized —
    /// only the first call after an interval elapses pays for the re-anchor.
    /// </summary>
    public static TimeSpan AdjustInterval
    {
        get => TimeSpan.FromSeconds((double)Volatile.Read(ref _adjustIntervalTicks) / ClockMath.StopwatchFrequency);
        set => Volatile.Write(ref _adjustIntervalTicks, Math.Max(0L, (long)(value.TotalSeconds * ClockMath.StopwatchFrequency)));
    }

    /// <summary>Current Unix time in milliseconds, tracking the system clock.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long UnixTimeMillisecondsNow()
    {
        // Re-anchor first (if due) so we project against the freshest origin.
        var now = AdjustClockMaybe();
        return Volatile.Read(ref _origin).ToUnixMilliseconds(now);
    }

    /// <summary>Current time as a UTC <see cref="DateTimeOffset"/>, tracking the system clock.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset DateTimeOffsetNow()
        => DateTimeOffset.FromUnixTimeMilliseconds(UnixTimeMillisecondsNow());

    // Hot path: sample the stopwatch and re-anchor only if the interval has elapsed. The rare re-anchor is
    // pushed out of line so the gate inlines cleanly.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long AdjustClockMaybe()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - Volatile.Read(ref _lastAdjustmentTicks) >= Volatile.Read(ref _adjustIntervalTicks))
            Adjust(now);
        return now;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Adjust(long now)
    {
        var last = Volatile.Read(ref _lastAdjustmentTicks);
        if (now - last < Volatile.Read(ref _adjustIntervalTicks))
            return;

        // Exactly one thread wins the interval and pays for the wall-clock read; the rest keep the current
        // origin until the next interval.
        if (Interlocked.CompareExchange(ref _lastAdjustmentTicks, now, last) != last)
            return;

        Volatile.Write(ref _origin, new ClockOrigin(now, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }
}
