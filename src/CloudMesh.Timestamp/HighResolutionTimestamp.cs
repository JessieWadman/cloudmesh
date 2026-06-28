using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System;

/// <summary>
/// Monotonic, high-resolution timestamp with millisecond-facing API.
/// Immune to DST. Immune to NTP jumps for elapsed-time calculations.
/// Ideal for timeouts, retries, cache expiration, sync intervals.
/// </summary>
public readonly record struct HighResolutionTimestamp : IComparable<HighResolutionTimestamp>
{
    private static readonly long StopwatchFrequency = Stopwatch.Frequency;
    private static readonly long OriginStopwatchTicks = Stopwatch.GetTimestamp();
    private static readonly long OriginUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly long stopwatchTicks;

    private HighResolutionTimestamp(long stopwatchTicks) => this.stopwatchTicks = stopwatchTicks;

    public static HighResolutionTimestamp Now => new(Stopwatch.GetTimestamp());
    public static readonly HighResolutionTimestamp Zero = new(0L);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long operator -(HighResolutionTimestamp a, HighResolutionTimestamp b)
        => StopwatchTicksToMilliseconds(a.stopwatchTicks - b.stopwatchTicks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Subtract(HighResolutionTimestamp left, HighResolutionTimestamp right)
        => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp operator -(HighResolutionTimestamp a, long milliseconds)
        => new(a.stopwatchTicks - MillisecondsToStopwatchTicks(milliseconds));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp Subtract(HighResolutionTimestamp left, long right)
        => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp operator +(HighResolutionTimestamp a, long milliseconds)
        => new(a.stopwatchTicks + MillisecondsToStopwatchTicks(milliseconds));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp Add(HighResolutionTimestamp left, long right)
        => left + right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToUnixTimeMilliseconds()
        => OriginUnixTimeMilliseconds + StopwatchTicksToMilliseconds(stopwatchTicks - OriginStopwatchTicks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToExactUnixTimeMilliseconds()
    {
        var exactNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var currentTicks = Stopwatch.GetTimestamp();

        return exactNow + StopwatchTicksToMilliseconds(stopwatchTicks - currentTicks);
    }

    /// <summary>
    /// Converts this timestamp to a UTC DateTimeOffset.
    ///
    /// Note: Conversion is performed using the static UTC/Stopwatch origin captured
    /// when the type was initialized. This avoids recomputing the exact UTC offset
    /// on every call.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToDateTimeOffset()
        => DateTimeOffset.FromUnixTimeMilliseconds(ToUnixTimeMilliseconds());

    /// <summary>
    /// Converts this timestamp to a UTC DateTimeOffset using the current UTC clock.
    ///
    /// Note: If the system clock has been adjusted since this timestamp was sampled,
    /// the resulting DateTimeOffset reflects the current understanding of UTC rather
    /// than the UTC time that was reported when the timestamp was originally captured.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToExactDateTimeOffset()
        => DateTimeOffset.FromUnixTimeMilliseconds(ToExactUnixTimeMilliseconds());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long StopwatchTicksToMilliseconds(long ticks)
    {
        var whole = ticks / StopwatchFrequency;
        var remainder = ticks % StopwatchFrequency;

        return whole * 1000L + remainder * 1000L / StopwatchFrequency;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long MillisecondsToStopwatchTicks(long milliseconds)
    {
        var whole = milliseconds / 1000L;
        var remainder = milliseconds % 1000L;

        return whole * StopwatchFrequency + remainder * StopwatchFrequency / 1000L;
    }

    public static bool operator <(HighResolutionTimestamp left, HighResolutionTimestamp right)
        => left.stopwatchTicks < right.stopwatchTicks;

    public static bool operator <=(HighResolutionTimestamp left, HighResolutionTimestamp right)
        => left.stopwatchTicks <= right.stopwatchTicks;

    public static bool operator >(HighResolutionTimestamp left, HighResolutionTimestamp right)
        => left.stopwatchTicks > right.stopwatchTicks;

    public static bool operator >=(HighResolutionTimestamp left, HighResolutionTimestamp right)
        => left.stopwatchTicks >= right.stopwatchTicks;

    public int CompareTo(HighResolutionTimestamp other)
        => stopwatchTicks.CompareTo(other.stopwatchTicks);

    public override int GetHashCode()
        => stopwatchTicks.GetHashCode();

    public override string ToString()
        => ToDateTimeOffset().ToString("O");
}