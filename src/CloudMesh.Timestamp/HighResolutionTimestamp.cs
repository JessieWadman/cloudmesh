using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System;

/// <summary>
/// Monotonic, high-resolution timestamp with a millisecond-facing API, backed by <see cref="Stopwatch"/>.
/// Elapsed-time math (the operators) is immune to wall-clock, NTP and DST changes. Wall-clock projections
/// (<see cref="ToUnixTimeMilliseconds"/> / <see cref="ToDateTimeOffset"/>) use a fixed origin captured at
/// process start, so they can drift from the system clock over a long-running process — use the
/// <c>ToExact…</c> variants, or <see cref="FastClock"/>, when the projected time must track the system clock.
/// Ideal for timeouts, retries, cache expiration and sync intervals.
/// </summary>
public readonly record struct HighResolutionTimestamp : IComparable<HighResolutionTimestamp>
{
    private readonly long stopwatchTicks;

    private HighResolutionTimestamp(long stopwatchTicks) => this.stopwatchTicks = stopwatchTicks;

    public static HighResolutionTimestamp Now => new(Stopwatch.GetTimestamp());

    public static readonly HighResolutionTimestamp Zero = new(0L);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long operator -(HighResolutionTimestamp a, HighResolutionTimestamp b)
        => ClockMath.StopwatchTicksToMilliseconds(a.stopwatchTicks - b.stopwatchTicks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Subtract(HighResolutionTimestamp left, HighResolutionTimestamp right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp operator -(HighResolutionTimestamp a, long milliseconds)
        => new(a.stopwatchTicks - ClockMath.MillisecondsToStopwatchTicks(milliseconds));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp Subtract(HighResolutionTimestamp left, long right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp operator +(HighResolutionTimestamp a, long milliseconds)
        => new(a.stopwatchTicks + ClockMath.MillisecondsToStopwatchTicks(milliseconds));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp Add(HighResolutionTimestamp left, long right) => left + right;

    /// <summary>Projects to Unix milliseconds using the fixed process-start origin (fast; may drift from the system clock).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToUnixTimeMilliseconds() => ClockOrigin.Startup.ToUnixMilliseconds(stopwatchTicks);

    /// <summary>Projects to Unix milliseconds by re-reading the current system clock (accurate; not drift-prone).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToExactUnixTimeMilliseconds()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
           + ClockMath.StopwatchTicksToMilliseconds(stopwatchTicks - Stopwatch.GetTimestamp());

    /// <summary>Converts to a UTC <see cref="DateTimeOffset"/> using the fixed process-start origin (fast; may drift).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(ToUnixTimeMilliseconds());

    /// <summary>Converts to a UTC <see cref="DateTimeOffset"/> by re-reading the current system clock (accurate).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToExactDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(ToExactUnixTimeMilliseconds());

    public static bool operator <(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks < right.stopwatchTicks;
    public static bool operator <=(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks <= right.stopwatchTicks;
    public static bool operator >(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks > right.stopwatchTicks;
    public static bool operator >=(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks >= right.stopwatchTicks;

    public int CompareTo(HighResolutionTimestamp other) => stopwatchTicks.CompareTo(other.stopwatchTicks);

    public override int GetHashCode() => stopwatchTicks.GetHashCode();

    public override string ToString() => ToDateTimeOffset().ToString("O");
}
