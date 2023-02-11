using System;
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

        public static decimal ToTez(this long mtz) =>
            (decimal)mtz / MtzInTz;

        public static decimal ToTez(this BigInteger mtz) =>
            mtz.ToDecimal(Decimals);

        public static long ToMicroTez(this decimal tz) =>
            (long)(tz * MtzInTz);

        //public static BigInteger ToTokens(this decimal value, int decimals) =>
        //    value.Multiply(BigInteger.Pow(10, decimals));

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