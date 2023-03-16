using System;

namespace Atomex.Common
{
    public static class DateTimeExtensions
    {
        public static DateTime UnixStartTime = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime ToUtcDateTime(this int unixTime) =>
            UnixStartTime.AddSeconds(unixTime);

        public static DateTime ToUtcDateTimeFromMs(this long unixTimeInMs) =>
            UnixStartTime.AddMilliseconds(unixTimeInMs);

        public static long ToUnixTime(this DateTime dt) =>
            (long)Math.Floor((dt - UnixStartTime).TotalSeconds);

        public static long ToUnixTimeMs(this DateTime dt) =>
            (long)Math.Floor((dt - UnixStartTime).TotalMilliseconds);

        public static DateTime FromHexString(this string hex) =>
            UnixStartTime.AddSeconds(int.Parse(hex, System.Globalization.NumberStyles.HexNumber));

        public static long ToUnixTimeSeconds(this DateTime dateTime) =>
            ((DateTimeOffset)dateTime.ToUniversalTime()).ToUnixTimeSeconds();

        public static string ToUtcIso8601(this DateTimeOffset dateTimeOffset) =>
            dateTimeOffset.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'");

        public static string ToIso8601(this DateTimeOffset dateTimeOffset) =>
            $"{dateTimeOffset.UtcDateTime:yyyy-MM-ddTHH:mm:ssK}";

        public static DateTimeOffset FromUnixTimeSeconds(this ulong seconds) =>
            DateTimeOffset.FromUnixTimeSeconds((long)seconds);

        public static string ToIso8601(this ulong seconds) =>
            FromUnixTimeSeconds(seconds).ToIso8601();
    }
}