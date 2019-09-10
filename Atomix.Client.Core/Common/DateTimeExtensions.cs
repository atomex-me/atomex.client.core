using System;

namespace Atomix.Common
{
    public static class DateTimeExtensions
    {
        public static DateTime UnixStartTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime ToUtcDateTime(this int unixTime)
        {
            return UnixStartTime.AddSeconds(unixTime);
        }

        public static DateTime ToUtcDateTime(this long unixTime)
        {
            return UnixStartTime.AddSeconds(unixTime);
        }

        public static double ToUnixTime(this DateTime dt)
        {
            return Math.Floor((dt - UnixStartTime).TotalSeconds);
        }

        public static long ToUnixTimeMs(this DateTime dt)
        {
            return (long)Math.Floor((dt - UnixStartTime).TotalMilliseconds);
        }

        public static bool EqualToMinutes(this DateTime dt, DateTime d)
        {
            return dt.Year == d.Year &&
                   dt.Month == d.Month &&
                   dt.Day == d.Day &&
                   dt.Hour == d.Hour &&
                   dt.Minute == d.Minute;
        }

        public static DateTime ToMinutes(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, second: 0);
        }
    }
}