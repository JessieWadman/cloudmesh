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
/// Time sortable UUID v7 implementation
/// </summary>
/// <remarks>
/// The timestamp comes from <see cref="FastClock"/>, which tracks the system clock (re-anchoring on NTP
/// corrections and VM suspend/resume), so the time embedded in the UUID stays accurate while generation
/// remains fast and allocation-free.
///
/// Based on the dotnet 9 implementation, but using Xoshiro256** for random generation, rather than the OLE32 interop call
/// CoCreateGuid(out Guid g) used by Guid.NewGuid()
/// The entire counter is random. We're not creating a 'node ID' for the first 12-42 bits.
/// </remarks>
public static class Uuid
{
    public static Guid Create() => Next(FastClock.UnixTimeMillisecondsNow());

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

    public static Guid Next(DateTimeOffset timestamp) => Next(timestamp.ToUnixTimeMilliseconds());
    
#if (NET8_0_OR_GREATER)        
    public static Guid Next(TimeProvider? timeProvider = null) => Next((timeProvider ?? TimeProvider.System).GetUtcNow());
#endif
}