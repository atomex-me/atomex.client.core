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
        public const int InternalError = 1005;

        public const int GetBalanceError = 1100;
        public const int GetTransactionError = 1101;
        public const int GetOutputsError = 1102;
        public const int GetTransactionsCountError = 1103;
        public const int GetFastGasPriceError = 1104;
        public const int GetGasLimitError = 1105;
        public const int GetReceiptStatusError = 1106;
        public const int GetRecentBlockHeightError = 1107;
        public const int GetGasPriceError = 1108;
        public const int GetErc20BalanceError = 1109;
        public const int GetErc20TransactionsError = 1110;
        public const int GetInternalTransactionsError = 1111;
        public const int GetTransactionsError = 1112;
        public const int GetBlockNumberError = 1113;

        // wallet errors
        public const int OutputsLockedError = 1300;
        public const int WalletNotFoundError = 1301;
        public const int WalletError = 1302;

        // ethereum errors
        public const int NullGasPriceError = 1400;
        public const int NullGasLimitError = 1401;
        public const int NullNonceError = 1402;

        // tezos errors
        public const int GetAccountError = 1501;
        public const int GetHeadError = 1502;
        public const int IsRevealedError = 1503;
        public const int GetCounterError = 1504;
        public const int RunOperationsError = 1505;
        public const int AutoFillError = 1506;
        public const int GetOperationsError = 1507;
        public const int OperationBatchingError = 1508;
    }
}