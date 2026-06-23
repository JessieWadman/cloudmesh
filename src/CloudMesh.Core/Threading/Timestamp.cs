using System.Runtime.CompilerServices;

namespace CloudMesh.Threading;

/// <summary>
/// Monotonic, fast timestamp.
/// Immune to DST, Immune to NTP jumps.
/// Ideal for timeouts, retries, cache expiration, sync intervals
/// </summary>
public readonly record struct Timestamp : IComparable<Timestamp>
{
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
    
    private static DateTimeOffset ToExactUtcOffset(long timestamp)
    {
        var exactNow = DateTimeOffset.UtcNow;
        var currentTicks = Environment.TickCount64;
        
        return exactNow.AddMilliseconds(timestamp - currentTicks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToUnixTimeMilliseconds() => ToDateTimeOffset().ToUnixTimeMilliseconds();

    /// <summary>
    /// Converts this timestamp to a UTC DateTimeOffset.
    ///
    /// Note: Conversion is performed using the current UTC clock and current
    /// Environment.TickCount64 value. If the system clock has been adjusted
    /// since this timestamp was sampled, the resulting DateTimeOffset reflects
    /// the current understanding of UTC rather than the UTC time that was
    /// reported when the timestamp was originally captured.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ToDateTimeOffset() => ToExactUtcOffset(milliseconds);
    
    public static bool operator <(Timestamp left, Timestamp right)
        => left.milliseconds < right.milliseconds;

    public static bool operator <=(Timestamp left, Timestamp right)
        => left.milliseconds <= right.milliseconds;

    public static bool operator >(Timestamp left, Timestamp right)
        => left.milliseconds > right.milliseconds;

    public static bool operator >=(Timestamp left, Timestamp right)
        => left.milliseconds >= right.milliseconds;

    public int CompareTo(Timestamp other) => this.milliseconds.CompareTo(other.milliseconds);

    public override int GetHashCode() => milliseconds.GetHashCode();

    public override string ToString()
    {
        return ToDateTimeOffset().ToString("O");
    }
}