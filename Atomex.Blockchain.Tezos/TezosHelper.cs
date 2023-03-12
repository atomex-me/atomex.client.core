using System.Numerics;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public static class TezosHelper
    {
        public const int Decimals = 6;
        public const int MtzInTz = 1000000;
        public const string Xtz = "XTZ";
        public const string Fa12 = "FA12";
        public const string Fa2 = "FA2";

        public static decimal ToTez(this long mtz) =>
            (decimal)mtz / MtzInTz;

        public static decimal ToTez(this int mtz) =>
            (decimal)mtz / MtzInTz;

        public static decimal ToTez(this BigInteger mtz) =>
            mtz.ToDecimal(Decimals);

        public static long ToMicroTez(this decimal tz) =>
            (long)(tz * MtzInTz);

        public static TransactionStatus ParseOperationStatus(this string status) =>
            status switch
            {
                "applied"     => TransactionStatus.Confirmed,
                "failed"      => TransactionStatus.Failed,
                "backtracked" => TransactionStatus.Failed,
                "skipped"     => TransactionStatus.Failed,
                _             => TransactionStatus.Confirmed
            };
    }
}