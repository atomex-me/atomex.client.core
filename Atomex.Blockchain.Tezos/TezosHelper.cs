using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Tezos
{
    public static class TezosHelper
    {
        public const int MtzInTz = 1000000;
        public const string Xtz = "XTZ";

        public static decimal ToTez(this long mtz) =>
            mtz / MtzInTz;

        public static long ToMicroTez(this decimal tz) =>
            (long)(tz * MtzInTz);

        public static TransactionStatus ParseOperationStatus(this string status) =>
            status switch
            {
                "applied"     => TransactionStatus.Confirmed,
                "failed"      => TransactionStatus.Canceled,
                "backtracked" => TransactionStatus.Canceled,
                "skipped"     => TransactionStatus.Canceled,
                _             => TransactionStatus.Confirmed
            };
    }
}