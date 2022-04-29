namespace Atomex.Common
{
    public static class Errors
    {
        // general errors
        public const int NotSupportedError = 1000;
        public const int SigningError = 1001;
        public const int BroadcastError = 1002;
        public const int VerificationError = 1003;
        public const int SendingError = 1004;

        public const int GetBalanceError = 1100;
        public const int GetTransactionError = 1101;
        public const int GetOutputsError = 1102;
        public const int GetReceiptStatusError = 1106;
        public const int GetRecentBlockHeightError = 1107;
        public const int GetGasPriceError = 1108;
        public const int GetErc20BalanceError = 1109;
        public const int GetErc20TransactionsError = 1110;
        public const int GetInternalTransactionsError = 1111;
        public const int GetTransactionsError = 1112;
        public const int GetBlockNumberError = 1113;
    }
}