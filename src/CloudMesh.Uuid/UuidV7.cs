using System.Runtime.CompilerServices;
using CloudMesh.Internal;

namespace CloudMesh;

// This is the exact same implementation as dotnet 9, except we're using Random.Shared (XoShiro implementation) to fill
// the counter-bits instead of relying on a legacy Ole32 interop call (which is a lot slower). We also rely on
// FastClock.UnixTimeMillisecondsNow() to generate the timestamp, which is a lot faster than
// DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().
// Random.Shared is thread-safe (a lock-free, per-thread Xoshiro), so Create()/Next() can be called concurrently.

/* Benchmarks:
| Method               | Mean     | Error    | StdDev   | Allocated |
|--------------------- |---------:|---------:|---------:|----------:|
| DotNetNewGuid        | 30.70 ns | 0.287 ns | 0.254 ns |         - | Guid.NewGuid() - Baseline
| DotNetCreateVersion7 | 49.59 ns | 0.276 ns | 0.231 ns |         - | Guid.CreateVersion7()
| CloudMeshUuid        | 20.08 ns | 0.142 ns | 0.133 ns |         - | Uuid.Create()
   
We can see that this is the fastest approach to generating a v7 compatible UUID.
In dotnet 10, Guid.CreateVersion7() uses DateTimeOffset.Now.ToUnixTimeMilliseconds() to generate the timestamp, which is a lot slower than using a high resolution timestamp.
It also uses Ole32.CoCreateGuid() which is also slower than using a random number generator.
Therein lies the reason why Guid.CreateVersion7() is so slow.
*/

/// <summary>
/// Generates time-sortable <see cref="Guid"/> values that conform to UUID version 7 (RFC 9562) — faster and
/// allocation-free compared to <see cref="Guid.NewGuid"/> and <c>Guid.CreateVersion7()</c>.
/// </summary>
/// <remarks>
/// <para>
/// A v7 UUID puts a 48-bit Unix-millisecond timestamp in its high bits, so values generated over time sort
/// (both as text and as bytes) in roughly chronological order. That makes them far friendlier to database
/// index locality than random <see cref="Guid"/>s while remaining 128-bit globally unique. Returned as a plain
/// <see cref="Guid"/>, so they drop into any API expecting one (EF Core keys, <c>Guid</c> columns, etc.).
/// </para>
/// <para>
/// The timestamp comes from <see cref="FastClock"/>, which tracks the system clock (re-anchoring on NTP
/// corrections and VM suspend/resume), so the time embedded in the UUID stays accurate while generation
/// remains fast and allocation-free. The bits below the timestamp are filled entirely from
/// <see cref="Random.Shared"/> (Xoshiro256**); there is no per-machine node id and no monotonic counter, so
/// two UUIDs minted in the same millisecond have no guaranteed ordering relative to each other. If you need a
/// strictly increasing, node-aware id, use a snowflake-style id instead.
/// </para>
/// <para>
/// Based on the .NET 9 implementation, but using Xoshiro256** for random generation rather than the OLE32
/// interop call <c>CoCreateGuid</c> used by <see cref="Guid.NewGuid"/>. All entropy bits are random.
/// </para>
/// <example>
/// <code>
/// Guid id = Uuid.Create();                       // v7 UUID stamped with "now"
/// Guid backfilled = Uuid.Next(createdAt);        // v7 UUID for a known DateTimeOffset
/// </code>
/// </example>
/// </remarks>
public static class Uuid
{
    /// <summary>
    /// Creates a new UUID v7 stamped with the current time (from <see cref="FastClock"/>). This is the common
    /// entry point and is the fastest, allocation-free path.
    /// </summary>
    /// <returns>A time-sortable v7 <see cref="Guid"/>.</returns>
    public static Guid Create() => Next(FastClock.UnixTimeMillisecondsNow());

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