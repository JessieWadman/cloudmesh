using System.Globalization;

namespace System
{
    /// <summary>
    /// Date/time conversion helpers: Unix-time round-tripping for <see cref="DateOnly"/>, common string formats
    /// (ISO 8601, XML, <c>yyyy-MM-dd</c>), a compact <c>yyyyMMdd</c> integer form, and time-truncation helpers.
    /// </summary>
    public static class DateHelper
    {
        /// <summary>Returns the Unix time in seconds for midnight (UTC) of the given date.</summary>
        public static long ToUnixTimeSeconds(this DateOnly dateOnly)
            => new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue)).ToUnixTimeSeconds();

        /// <summary>Returns the Unix time in milliseconds for midnight (UTC) of the given date.</summary>
        public static long ToUnixTimeMilliseconds(this DateOnly dateOnly)
            => new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

        /// <summary>Returns the <see cref="DateOnly"/> for the given Unix time in seconds.</summary>
        public static DateOnly FromUnixTimeSeconds(long value)
            => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(value).Date);

        /// <summary>Returns the <see cref="DateOnly"/> for the given Unix time in milliseconds.</summary>
        public static DateOnly FromUnixTimeMilliseconds(long value)
            => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(value).Date);

        /// <summary>Formats the value as UTC <c>yyyy-MM-ddTHH:mm:ss</c> (XML dateTime, no fractional seconds).</summary>
        public static string ToXmlString(this DateTime dt)
            => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss");

        /// <summary>Formats the value as UTC ISO 8601 with milliseconds and a trailing <c>Z</c> (<c>yyyy-MM-ddTHH:mm:ss.fffZ</c>).</summary>
        public static string ToISO8601(this DateTimeOffset dt)
            => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        /// <summary>Parses an ISO 8601 / RFC 3339 date-time string into a <see cref="DateTimeOffset"/>.</summary>
        public static DateTimeOffset FromISO8601String(string source)
            => DateTimeOffset.Parse(source);

        /// <summary>Formats the value as <c>yyyy-MM-dd</c> using the invariant culture.</summary>
        public static string ToYMD(this DateTime dt)
            => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Formats the value as <c>yyyy-MM-dd HH:mm:ss</c> using the invariant culture.</summary>
        public static string ToFriendlyString(this DateTime dt)
            => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>Returns the date as a compact integer in <c>yyyyMMdd</c> form (e.g. 2024-01-31 → 20240131).</summary>
        public static int DateOnlyInteger(this DateTimeOffset dt)
            => int.Parse(dt.ToString("yyyyMMdd"));

        /// <summary>Returns the date as a compact integer in <c>yyyyMMdd</c> form (e.g. 2024-01-31 → 20240131).</summary>
        public static int DateOnlyInteger(this DateTime dt)
           => int.Parse(dt.ToString("yyyyMMdd"));

        /// <summary>Returns the same date at midnight, preserving the original UTC offset.</summary>
        public static DateTimeOffset WithNoTime(this DateTimeOffset dt)
        {
            return new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
        }

        /// <summary>Returns the same date at midnight (time component removed).</summary>
        public static DateTime WithNoTime(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0);
        }
    }
}
