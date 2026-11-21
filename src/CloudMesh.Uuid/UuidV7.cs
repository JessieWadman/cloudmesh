using System.Runtime.CompilerServices;
using CloudMesh.Internal;

namespace CloudMesh;

// Uuid.Create() mints UUID v7 values that are strictly increasing within a process (RFC 9562 §6.2 method 1:
// a counter directly below the timestamp). Entropy comes from Random.Shared (a per-thread, lock-free
// Xoshiro256**), not a CSPRNG: these ids are meant for message ids and time-sortable primary keys, not as
// unguessable tokens. The timestamp comes from FastClock, which is cheaper than DateTimeOffset.UtcNow.
//
// Benchmarks (.NET 11, x64, short runs). Contended = wall time per id with N threads minting at once.
// | Method                | 1 thread | 4 threads | 32 threads |
// |---------------------- |---------:|----------:|-----------:|
// | Guid.NewGuid()        |  ~34 ns  |  ~12 ns   |  ~2.8 ns   |
// | Guid.CreateVersion7() |  ~53 ns  |  ~20 ns   |  ~4.1 ns   |
// | Uuid.Create()         |  ~25 ns  |  ~19 ns   |   ~29 ns   |
// Ordering costs ~3 ns per id on one thread. Under heavy contention every caller takes turns on one shared
// 64-bit word, so throughput plateaus at roughly 35-50M ids/s per process instead of scaling with cores.

/// <summary>
/// Generates time-sortable <see cref="Guid"/> values that conform to UUID version 7 (RFC 9562).
/// <see cref="Create"/> is strictly increasing within a process, across threads.
/// </summary>
/// <remarks>
/// <para>
/// A v7 UUID puts a 48-bit Unix-millisecond timestamp in its high bits, so values generated over time sort in
/// chronological order when compared in RFC (big-endian) byte order. Returned as a plain <see cref="Guid"/>,
/// so they drop into any API expecting one (EF Core keys, <c>Guid</c> columns, etc.).
/// </para>
/// <para>
/// <see cref="Create"/> places a 16-bit counter directly below the timestamp, so ids minted in the same
/// millisecond keep their generation order. The <c>Next</c> overloads stamp a caller-supplied time and do
/// not use the counter: their random bits are not ordered, and they never affect what <see cref="Create"/>
/// returns.
/// </para>
/// <para>
/// Random bits come from <see cref="Random.Shared"/> (Xoshiro256**), which is fast but not cryptographically
/// secure. Don't use these ids as unguessable tokens. Also note that a process restored from the same
/// snapshot more than once (e.g. Lambda SnapStart, VM clones) resumes with identical generator state.
/// </para>
/// <example>
/// <code>
/// Guid id = Uuid.Create();                       // v7 UUID stamped with "now", strictly increasing
/// Guid backfilled = Uuid.Next(createdAt);        // v7 UUID for a known DateTimeOffset
/// </code>
/// </example>
/// </remarks>
public static class Uuid
{
    /// <summary>
    /// Creates a new UUID v7 stamped with the current time. Every value is strictly greater than every value
    /// previously returned by this method in the current process, across all threads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements RFC 9562 §6.2 method 1 (fixed-length dedicated counter). A 16-bit counter sits directly below
    /// the 48-bit timestamp: its high 12 bits fill <c>rand_a</c> and its low 4 bits the top of <c>rand_b</c>,
    /// below the variant. The remaining 58 bits are random.
    /// </para>
    /// <para>
    /// Timestamp and counter advance together as a single 64-bit word through a lock-free compare-and-swap.
    /// Each new millisecond seeds the counter with a random 15-bit value (its top bit is a guard that leaves
    /// room to count up), so ids from different processes keep about the same collision resistance as fully
    /// random bits. If the system clock steps backwards, or more than about 32k ids are minted in one
    /// millisecond, the counter carries into the timestamp. Ids may then run slightly ahead of the clock, but
    /// they never repeat or go backwards.
    /// </para>
    /// <para>
    /// Ordering is per process only. All callers share one word, so under heavy multi-threaded load
    /// throughput plateaus (roughly 35-50M ids/s per process) rather than scaling with cores.
    /// </para>
    /// </remarks>
    /// <returns>A time-sortable v7 <see cref="Guid"/>, strictly increasing within this process.</returns>
    public static Guid Create()
    {
        var ticket = MonotonicState.NextTicket(FastClock.UnixTimeMillisecondsNow());
        return FromTicket(ticket);
    }

    /// <summary>
    /// Creates a UUID v7 whose timestamp bits carry the supplied Unix-millisecond value. Useful for
    /// back-filling deterministic ids for records with a known creation time.
    /// </summary>
    /// <param name="unixTimestampMilliseconds">Milliseconds since the Unix epoch (must be non-negative).</param>
    /// <returns>A v7 <see cref="Guid"/> whose high bits encode <paramref name="unixTimestampMilliseconds"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unixTimestampMilliseconds"/> is negative.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe Guid Next(long unixTimestampMilliseconds)
    {
#if (NET8_0_OR_GREATER)        
        ArgumentOutOfRangeException.ThrowIfNegative(unixTimestampMilliseconds, nameof(unixTimestampMilliseconds));
#else
        if (unixTimestampMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(unixTimestampMilliseconds));
#endif

        var uuid = new UuidLayout();
        // We're overwriting the _a and _b, so we could ask Random to only fill the remaining bytes.
        // However, dotnet uses Xoshiro256 under the hood, which operates on ulongs (8 bytes), so it's actually faster
        // to code for the hot-path and just fill the entire 16 bytes, since it's exactly 2 random ulongs, rather than forcing
        // System.Random to fall back to a slower code path that accommodates for byte arrays that aren't divisible by 8.
        Span<byte> bytes = new(uuid.Bytes, 16);
        Random.Shared.NextBytes(bytes);

        Unsafe.AsRef(in uuid.GuidFields._a) = (int)(unixTimestampMilliseconds >> 16);
        Unsafe.AsRef(in uuid.GuidFields._b) = (short)(unixTimestampMilliseconds);
        Unsafe.AsRef(in uuid.GuidFields._c) = (short)((uuid.GuidFields._c & ~UuidConstants.VersionMask) | UuidConstants.Version7Value);
        Unsafe.AsRef(in uuid.GuidFields._d) = (byte)((uuid.GuidFields._d & ~UuidConstants.Variant10xxMask) | UuidConstants.Variant10xxValue);

        return uuid.Guid;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe Guid FromTicket(ulong ticket)
    {
        var uuid = new UuidLayout();
        Span<byte> bytes = new(uuid.Bytes, 16);
        Random.Shared.NextBytes(bytes);

        var unixMs = (long)(ticket >> MonotonicState.CounterBits);
        var counter = (int)(ticket & MonotonicState.CounterMask);

        Unsafe.AsRef(in uuid.GuidFields._a) = (int)(unixMs >> 16);
        Unsafe.AsRef(in uuid.GuidFields._b) = (short)unixMs;
        // version (4 bits) | counter high 12 bits -> rand_a
        Unsafe.AsRef(in uuid.GuidFields._c) = (short)(UuidConstants.Version7Value | (counter >> 4));
        // variant (2 bits) | counter low 4 bits | 2 random bits
        Unsafe.AsRef(in uuid.GuidFields._d) = (byte)(UuidConstants.Variant10xxValue | ((counter & 0xF) << 2) | (uuid.GuidFields._d & 0x3));

        return uuid.Guid;
    }

    /// <summary>Creates a UUID v7 whose timestamp bits carry the supplied instant.</summary>
    /// <param name="timestamp">The instant to embed. Its <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/> must be non-negative.</param>
    /// <returns>A v7 <see cref="Guid"/> stamped with <paramref name="timestamp"/>.</returns>
    public static Guid Next(DateTimeOffset timestamp) => Next(timestamp.ToUnixTimeMilliseconds());

#if (NET8_0_OR_GREATER)
    /// <summary>Creates a UUID v7 stamped with the current time as read from a <see cref="TimeProvider"/>.</summary>
    /// <param name="timeProvider">The clock to read; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>. Pass a fake provider to make id timestamps deterministic in tests.</param>
    /// <returns>A v7 <see cref="Guid"/> stamped with the provider's current time.</returns>
    public static Guid Next(TimeProvider? timeProvider = null) => Next((timeProvider ?? TimeProvider.System).GetUtcNow());
#endif
}