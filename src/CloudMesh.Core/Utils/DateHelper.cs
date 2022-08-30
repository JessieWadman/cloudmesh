using System.Globalization;

namespace System
{
    public static class DateHelper
    {
        public static long ToUnixTimeSeconds(this DateOnly dateOnly)
            => new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue)).ToUnixTimeSeconds();
        public static long ToUnixTimeMilliseconds(this DateOnly dateOnly)
            => new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

        public static DateOnly FromUnixTimeSeconds(long value)
            => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(value).Date);

        public static DateOnly FromUnixTimeMilliseconds(long value)
            => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(value).Date);

        public static string ToXmlString(this DateTime dt)
            => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss");

        public static string ToISO8601(this DateTimeOffset dt)
            => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        public static DateTimeOffset FromISO8601String(string source)
            => DateTimeOffset.Parse(source);

        public static string ToYMD(this DateTime dt)
            => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static string ToFriendlyString(this DateTime dt)
            => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        public static int DateOnlyInteger(this DateTimeOffset dt)
            => int.Parse(dt.ToString("yyyyMMdd"));

        public static int DateOnlyInteger(this DateTime dt)
           => int.Parse(dt.ToString("yyyyMMdd"));

        public static DateTimeOffset WithNoTime(this DateTimeOffset dt)
        {
            return new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
        }

        public static DateTime WithNoTime(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0);
        }
    }
}
