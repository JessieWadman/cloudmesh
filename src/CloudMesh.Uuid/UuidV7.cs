using System.Runtime.CompilerServices;
using CloudMesh.Internal;

namespace CloudMesh;

// This is the exact same implementation as dotnet 9, except we're using System.Random to fill the counter bits instead
// of relying on a legacy COM interop call (which is a lot slower).

/* Benchmarks:
    | Method                                          | Mean      | Error     | StdDev    |
    |------------------------------------------------ |----------:|----------:|----------:|
    | Uuid_v4_baseline_plain_old_Guid_NewGuid         | 29.652 ns | 0.0447 ns | 0.0396 ns | Guid.NewGuid()
    | Uuid_v7_ComInterop_NewGuid                      | 32.701 ns | 0.0389 ns | 0.0325 ns | Dotnet 9 implementation standard implementation (using fixed timestamp)
    | Uuid7_ComInterop_NewGuid_WithActualTime         | 65.456 ns | 1.2858 ns | 2.5679 ns | Dotnet 9 implementation using DateTimeOffset.Now.ToUnixTimeMilliseconds()
    | Uuid_v7_Xoshiro256                              |  6.197 ns | 0.0179 ns | 0.0159 ns | This implementation (dotnet uses xoshiro256 algorithm to generate random numbers) using fixed timestamp
    | Uuid_v7_Xoshiro256_WithActualTime               | 32.389 ns | 0.0115 ns | 0.0090 ns | This implementation when called with DateTimeOffset.Now.ToUnixTimeMilliseconds()
    | Uuid7_System_Cryptography_RandomNumberGenerator | 40.614 ns | 0.1770 ns | 0.1569 ns | Same implementation as this but using System.Cryptography.RandomNumberGenerator       
    
Here we can see that the DateTimeOffset.ToUnixTimeMilliseconds() adds a lot of overhead.
We can also see that this here is the fastest approach to generating a v7 compatible UUID.
 */

/// <summary>
/// Time sortable UUID v7 implementation
/// </summary>
/// <remarks>
/// Based on the dotnet 9 implementation, but using Xoshiro256** for random generation, rather than the OLE32 interop call
/// CoCreateGuid(out Guid g) used by Guid.NewGuid()
/// The entire counter is random. We're not creating a 'node ID' for the first 12-42 bits.
/// </remarks>
public static class Uuid
{
    private static readonly Random Random = new();

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
        Random.NextBytes(bytes);

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