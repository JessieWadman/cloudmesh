using System.Diagnostics;

namespace CloudMesh.Variant;

public readonly partial struct Value
{
    internal readonly struct PackedDateTimeOffset
    {
        // SHHHH QQ TTT...
        //
        // S       - offset sign (0 = non-negative, 1 = negative)
        // HHHH    - whole hours of |offset|, 0-14
        // QQ      - quarter-hour of |offset|'s remaining minutes
        //            00 - :00
        //            01 - :15
        //            10 - :30
        //            11 - :45
        // T...    - ticks, relative to BaseTicks

        // Base local tick time 1800 [DateTime(1800, 1, 1).Ticks]
        private const ulong BaseTicks = 567709344000000000;
        private const ulong MaxTicks = BaseTicks + TickMask;

        // Offset magnitude is split as whole hours (0-14) plus a quarter-hour remainder (0,15,30,45 minutes),
        // with sign stored separately. This avoids ever packing a signed minute count into an unsigned field
        // (the earlier encoding did `(ulong)(minutes / 15)` for a negative value, which corrupted every bit
        // above the minute field once left-shifted) and avoids scaling the quarter index incorrectly on the
        // way back out (the earlier decode used the raw 0-3 quarter index as a minute count, so +02:30 came
        // back as +02:02).
        private const ulong TickMask     = 0b00000001_11111111_11111111_11111111__11111111_11111111_11111111_11111111;
        private const ulong QuarterMask  = 0b00000110_00000000_00000000_00000000__00000000_00000000_00000000_00000000;
        private const ulong HoursMask    = 0b01111000_00000000_00000000_00000000__00000000_00000000_00000000_00000000;
        private const ulong SignMask     = 0b10000000_00000000_00000000_00000000__00000000_00000000_00000000_00000000;

        private const int QuarterShift = 57;
        private const int HoursShift = 59;

        private readonly ulong _data;

        private PackedDateTimeOffset(ulong data) => _data = data;

        public static bool TryCreate(DateTimeOffset dateTime, TimeSpan offset, out PackedDateTimeOffset packed)
        {
            packed = default;

            var ticks = (ulong)dateTime.Ticks;
            if (ticks is <= BaseTicks or >= MaxTicks)
                return false;

            // DateTimeOffset.Offset is always a whole number of minutes (the BCL enforces this), but we only
            // have 2 bits for the sub-hour remainder, so only quarter-hour-aligned offsets take the packed
            // fast path; anything else (e.g. a 13-minute offset) falls back to boxing in the caller.
            var totalMinutesAbs = (int)Math.Abs(offset.Ticks / TimeSpan.TicksPerMinute);
            if (totalMinutesAbs % 15 != 0)
                return false;

            var hoursAbs = totalMinutesAbs / 60;
            var quarterAbs = (totalMinutesAbs % 60) / 15;

            // Only valid offset magnitudes are 0 to 14 hours.
            Debug.Assert(hoursAbs is >= 0 and <= 14);

            ulong data = (ulong)quarterAbs << QuarterShift;
            data |= (ulong)hoursAbs << HoursShift;
            if (offset.Ticks < 0)
                data |= SignMask;
            data |= ticks - BaseTicks;
            packed = new(data);

            return true;
        }

        public DateTimeOffset Extract()
        {
            var hoursAbs = (int)((_data & HoursMask) >> HoursShift);
            var quarterAbs = (int)((_data & QuarterMask) >> QuarterShift);
            var totalMinutesAbs = hoursAbs * 60 + quarterAbs * 15;
            var totalMinutes = (_data & SignMask) != 0 ? -totalMinutesAbs : totalMinutesAbs;

            var offset = TimeSpan.FromMinutes(totalMinutes);
            return new((long)((_data & TickMask) + BaseTicks), offset);
        }
    }
}
