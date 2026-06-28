using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System;

internal static class ClockMath
{
    public static readonly long StopwatchFrequency = Stopwatch.Frequency;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long StopwatchTicksToMilliseconds(long ticks)
    {
        var whole = ticks / StopwatchFrequency;
        var remainder = ticks % StopwatchFrequency;

        return whole * 1000L + remainder * 1000L / StopwatchFrequency;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long MillisecondsToStopwatchTicks(long milliseconds)
    {
        var whole = milliseconds / 1000L;
        var remainder = milliseconds % 1000L;

        return whole * StopwatchFrequency + remainder * StopwatchFrequency / 1000L;
    }
}