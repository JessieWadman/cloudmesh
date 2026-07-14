using System.Runtime.CompilerServices;

namespace System;

/// <summary>
/// Monotonic, very cheap timestamp backed by <see cref="Environment.TickCount64"/> (millisecond resolution).
/// Elapsed-time math (the operators) is immune to wall-clock, NTP and DST changes. Wall-clock projections
/// use a fixed origin captured at process start (fast; may drift over a long-running process) — use the
/// <c>ToExact…</c> variants when the projected time must track the system clock. Ideal for timeouts, retries,
/// cache expiration and sync intervals.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Timestamp"/> is a single <see cref="long"/> — capturing <see cref="Now"/> is essentially a
/// register read, with no allocation and no kernel transition. Because the underlying counter only ever moves
/// forward, subtracting two timestamps yields a duration that can never go negative from a clock adjustment,
/// unlike <see cref="DateTimeOffset.UtcNow"/> arithmetic.
/// </para>
/// <para>
/// The trade-off: the value is <em>not</em> a wall-clock time. It becomes one only when projected via
/// <see cref="ToUnixTimeMilliseconds"/>/<see cref="ToDateTimeOffset"/> (fast, fixed origin) or the
/// <c>ToExact…</c> variants (re-read the system clock). For the finest resolution use
/// <see cref="HighResolutionTimestamp"/>; for a wall-clock reading that stays accurate over a long-running
/// process, use <see cref="FastClock"/>.
/// </para>
/// <example>
/// <code>
/// var start = Timestamp.Now;
/// DoWork();
/// long elapsedMs = Timestamp.Now - start;   // immune to NTP/DST changes
/// </code>
/// </example>
/// </remarks>
public readonly record struct Timestamp : IComparable<Timestamp>
{
    private static readonly long OriginTickCount = Environment.TickCount64;
    private static readonly long OriginUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly long milliseconds;

    private Timestamp(long milliseconds) => this.milliseconds = milliseconds;

    /// <summary>Captures the current monotonic timestamp. Extremely cheap; no allocation, no system-clock read.</summary>
    public static Timestamp Now => new(Environment.TickCount64);

    /// <summary>The zero timestamp (the epoch of the underlying monotonic counter). Useful as a sentinel/default.</summary>
    public static readonly Timestamp Zero = new(0L);

    /// <summary>Returns the elapsed time between two timestamps, in whole milliseconds.</summary>
    /// <returns>The number of milliseconds from <paramref name="b"/> to <paramref name="a"/> (negative if <paramref name="a"/> precedes <paramref name="b"/>).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long operator -(Timestamp a, Timestamp b) => a.milliseconds - b.milliseconds;

    /// <summary>Returns the elapsed milliseconds between two timestamps (operator-free alias of <c>left - right</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Subtract(Timestamp left, Timestamp right) => left - right;

    /// <summary>Shifts a timestamp earlier by the given number of milliseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp operator -(Timestamp a, long b) => new(a.milliseconds - b);

    /// <summary>Shifts a timestamp earlier by the given number of milliseconds (operator-free alias).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp Subtract(Timestamp left, long right) => left - right;

    /// <summary>Shifts a timestamp later by the given number of milliseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp operator +(Timestamp a, long b) => new(a.milliseconds + b);

    /// <summary>Shifts a timestamp later by the given number of milliseconds (operator-free alias).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp Add(Timestamp left, long right) => left + right;

    /// <summary>Projects to Unix milliseconds using the fixed process-start origin (fast; may drift from the system clock).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToUnixTimeMilliseconds() => OriginUnixMilliseconds + (milliseconds - OriginTickCount);

    /// <summary>Projects to Unix milliseconds by re-reading the current system clock (accurate; not drift-prone).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToExactUnixTimeMilliseconds()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (milliseconds - Environment.TickCount64);

    /// <summary>Converts to a UTC <see cref="DateTimeOffset"/> using the fixed process-start origin (fast; may drift).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(ToUnixTimeMilliseconds());

    /// <summary>Converts to a UTC <see cref="DateTimeOffset"/> by re-reading the current system clock (accurate).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToExactDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds(ToExactUnixTimeMilliseconds());

    /// <summary>Indicates whether <paramref name="left"/> occurred before <paramref name="right"/>.</summary>
    public static bool operator <(Timestamp left, Timestamp right) => left.milliseconds < right.milliseconds;
    /// <summary>Indicates whether <paramref name="left"/> occurred at or before <paramref name="right"/>.</summary>
    public static bool operator <=(Timestamp left, Timestamp right) => left.milliseconds <= right.milliseconds;
    /// <summary>Indicates whether <paramref name="left"/> occurred after <paramref name="right"/>.</summary>
    public static bool operator >(Timestamp left, Timestamp right) => left.milliseconds > right.milliseconds;
    /// <summary>Indicates whether <paramref name="left"/> occurred at or after <paramref name="right"/>.</summary>
    public static bool operator >=(Timestamp left, Timestamp right) => left.milliseconds >= right.milliseconds;

    /// <summary>Orders timestamps chronologically. Consistent with the comparison operators.</summary>
    public int CompareTo(Timestamp other) => milliseconds.CompareTo(other.milliseconds);

    /// <inheritdoc/>
    public override int GetHashCode() => milliseconds.GetHashCode();

    /// <summary>Renders the wall-clock projection (fixed origin) as a round-trip (<c>"O"</c>) UTC string.</summary>
    public override string ToString() => ToDateTimeOffset().ToString("O");
}
