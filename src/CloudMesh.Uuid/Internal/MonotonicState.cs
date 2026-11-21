using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CloudMesh.Internal;

/// <summary>
/// Process-wide state behind <see cref="Uuid.Create"/>: one 64-bit word packing
/// <c>(unixMilliseconds &lt;&lt; 16) | counter</c>, advanced lock-free with compare-and-swap.
/// </summary>
internal static class MonotonicState
{
    public const int CounterBits = 16;
    public const ulong CounterMask = (1UL << CounterBits) - 1;

    // Random seed range for each new millisecond. The counter's top bit stays clear (guard bit), leaving
    // at least 32k increments before a millisecond overflows into the timestamp.
    private const int SeedRange = 1 << (CounterBits - 1);

    // The hot word sits alone on its own cache line, so unrelated statics don't false-share with it.
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedTicket
    {
        [FieldOffset(64)] public ulong Value;
    }

    private static PaddedTicket _last;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NextTicket(long unixMilliseconds)
    {
        var floor = (ulong)unixMilliseconds << CounterBits;
        ref var last = ref _last.Value;
        while (true)
        {
            var current = Volatile.Read(ref last);
            // Same millisecond, a clock that stepped backwards, or a counter that already carried ahead:
            // keep incrementing. Otherwise this is a new millisecond, so seed the counter randomly.
            var next = current >= floor
                ? current + 1
                : floor | (uint)Random.Shared.Next(SeedRange);

            if (Interlocked.CompareExchange(ref last, next, current) == current)
                return next;
        }
    }
}
