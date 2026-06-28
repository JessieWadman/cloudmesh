using System.Runtime.CompilerServices;

namespace System;

/// <summary>
/// Monotonic, very cheap timestamp backed by <see cref="Environment.TickCount64"/> (millisecond resolution).
/// Elapsed-time math (the operators) is immune to wall-clock, NTP and DST changes. Wall-clock projections
/// use a fixed origin captured at process start (fast; may drift over a long-running process) — use the
/// <c>ToExact…</c> variants when the projected time must track the system clock. Ideal for timeouts, retries,
/// cache expiration and sync intervals.
/// </summary>
public readonly record struct Timestamp : IComparable<Timestamp>
{
    private static readonly long OriginTickCount = Environment.TickCount64;
    private static readonly long OriginUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly long milliseconds;

    private Timestamp(long milliseconds) => this.milliseconds = milliseconds;

    public static Timestamp Now => new(Environment.TickCount64);
    public static readonly Timestamp Zero = new(0L);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long operator -(Timestamp a, Timestamp b) => a.milliseconds - b.milliseconds;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Subtract(Timestamp left, Timestamp right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp operator -(Timestamp a, long b) => new(a.milliseconds - b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp Subtract(Timestamp left, long right) => left - right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Timestamp operator +(Timestamp a, long b) => new(a.milliseconds + b);

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

    public static bool operator <(Timestamp left, Timestamp right) => left.milliseconds < right.milliseconds;
    public static bool operator <=(Timestamp left, Timestamp right) => left.milliseconds <= right.milliseconds;
    public static bool operator >(Timestamp left, Timestamp right) => left.milliseconds > right.milliseconds;
    public static bool operator >=(Timestamp left, Timestamp right) => left.milliseconds >= right.milliseconds;

    public int CompareTo(Timestamp other) => milliseconds.CompareTo(other.milliseconds);

    public override int GetHashCode() => milliseconds.GetHashCode();

    public override string ToString() => ToDateTimeOffset().ToString("O");
}
