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
/// <remarks>
/// The difference from <see cref="Timestamp"/> is resolution and source: this reads
/// <see cref="Stopwatch.GetTimestamp"/> (sub-millisecond hardware counter) rather than the coarser
/// <see cref="Environment.TickCount64"/>. The arithmetic operators still speak whole milliseconds, but the
/// captured instant is precise, so it is the right choice when you need to distinguish events that fall in the
/// same millisecond (for example, snowflake-style id generators use it for their timestamp component).
/// </remarks>
public readonly record struct HighResolutionTimestamp : IComparable<HighResolutionTimestamp>
{
    private readonly long stopwatchTicks;

    private HighResolutionTimestamp(long stopwatchTicks) => this.stopwatchTicks = stopwatchTicks;

    /// <summary>Captures the current instant from the high-resolution monotonic counter. No allocation, no system-clock read.</summary>
    public static HighResolutionTimestamp Now => new(Stopwatch.GetTimestamp());

    /// <summary>The zero timestamp (origin of the underlying counter). Useful as a sentinel/default.</summary>
    public static readonly HighResolutionTimestamp Zero = new(0L);

    /// <summary>Returns the elapsed time between two timestamps, in whole milliseconds.</summary>
    /// <returns>The number of milliseconds from <paramref name="b"/> to <paramref name="a"/> (negative if <paramref name="a"/> precedes <paramref name="b"/>).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long operator -(HighResolutionTimestamp a, HighResolutionTimestamp b)
        => ClockMath.StopwatchTicksToMilliseconds(a.stopwatchTicks - b.stopwatchTicks);

    /// <summary>Returns the elapsed milliseconds between two timestamps (operator-free alias of <c>left - right</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Subtract(HighResolutionTimestamp left, HighResolutionTimestamp right) => left - right;

    /// <summary>Shifts a timestamp earlier by the given number of milliseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp operator -(HighResolutionTimestamp a, long milliseconds)
        => new(a.stopwatchTicks - ClockMath.MillisecondsToStopwatchTicks(milliseconds));

    /// <summary>Shifts a timestamp earlier by the given number of milliseconds (operator-free alias).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp Subtract(HighResolutionTimestamp left, long right) => left - right;

    /// <summary>Shifts a timestamp later by the given number of milliseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HighResolutionTimestamp operator +(HighResolutionTimestamp a, long milliseconds)
        => new(a.stopwatchTicks + ClockMath.MillisecondsToStopwatchTicks(milliseconds));

    /// <summary>Shifts a timestamp later by the given number of milliseconds (operator-free alias).</summary>
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

    /// <summary>Indicates whether <paramref name="left"/> occurred before <paramref name="right"/>.</summary>
    public static bool operator <(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks < right.stopwatchTicks;
    /// <summary>Indicates whether <paramref name="left"/> occurred at or before <paramref name="right"/>.</summary>
    public static bool operator <=(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks <= right.stopwatchTicks;
    /// <summary>Indicates whether <paramref name="left"/> occurred after <paramref name="right"/>.</summary>
    public static bool operator >(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks > right.stopwatchTicks;
    /// <summary>Indicates whether <paramref name="left"/> occurred at or after <paramref name="right"/>.</summary>
    public static bool operator >=(HighResolutionTimestamp left, HighResolutionTimestamp right) => left.stopwatchTicks >= right.stopwatchTicks;

    /// <summary>Orders timestamps chronologically. Consistent with the comparison operators.</summary>
    public int CompareTo(HighResolutionTimestamp other) => stopwatchTicks.CompareTo(other.stopwatchTicks);

    /// <inheritdoc/>
    public override int GetHashCode() => stopwatchTicks.GetHashCode();

    /// <summary>Renders the wall-clock projection (fixed origin) as a round-trip (<c>"O"</c>) UTC string.</summary>
    public override string ToString() => ToDateTimeOffset().ToString("O");
}
