using System;

namespace Atomex.Blockchain.Tezos.Common
{
    public static class DateTimeExtensions
    {
        public static string ToIso8601(this DateTimeOffset dateTimeOffset) =>
            $"{dateTimeOffset.UtcDateTime:yyyy-MM-ddTHH:mm:ssK}";

        public static DateTimeOffset FromUnixTimeSeconds(this ulong seconds) =>
            DateTimeOffset.FromUnixTimeSeconds((long)seconds);

        public static string ToIso8601(this ulong seconds) =>
            FromUnixTimeSeconds(seconds).ToIso8601();
    }
}