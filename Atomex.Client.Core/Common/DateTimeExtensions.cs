using System;

namespace Atomex.Common
{
    public static class DateTimeExtensions
    {
        public static DateTime UnixStartTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime ToUtcDateTime(this int unixTime) =>
            UnixStartTime.AddSeconds(unixTime);

        public static DateTime ToUtcDateTime(this long unixTime) =>
            UnixStartTime.AddSeconds(unixTime);

        public static long ToUnixTime(this DateTime dt) =>
            (long)Math.Floor((dt - UnixStartTime).TotalSeconds);

        public static long ToUnixTimeMs(this DateTime dt) =>
            (long)Math.Floor((dt - UnixStartTime).TotalMilliseconds);

        public static bool EqualToMinutes(this DateTime dt, DateTime d) =>
            dt.Year == d.Year &&
            dt.Month == d.Month &&
            dt.Day == d.Day &&
            dt.Hour == d.Hour &&
            dt.Minute == d.Minute;

        public static DateTime ToMinutes(this DateTime dt) =>
            new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, second: 0);

        public static DateTime FromHexString(this string hex) =>
            UnixStartTime.AddSeconds(int.Parse(hex, System.Globalization.NumberStyles.HexNumber));

        public static long ToUnixTimeSeconds(this DateTime dateTime) =>
            ((DateTimeOffset)dateTime.ToUniversalTime()).ToUnixTimeSeconds();
    }
}