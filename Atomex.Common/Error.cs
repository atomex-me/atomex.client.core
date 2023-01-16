using System;

namespace Atomex.Common
{
    public readonly struct Error
    {
        public int Code { get; init; }
        public string Message { get; init; }
        public Exception Exception { get; init; }

        public Error(int code, string message)
        {
            Code = code;
            Message = message;
            Exception = null;
        }

        public Error(int code, string message, Exception exception)
        {
            Code = code;
            Message = message;
            Exception = exception;
        }
    }

    public static class Errors
    {
        public const int NoError = 0;
        public const int NotAuthorized = 1;
        public const int AuthError = 2;
        public const int InvalidRequest = 3;
        public const int InvalidMessage = 4;
        public const int PermissionError = 5;
        public const int InternalError = 6;
        public const int RequestError = 7;
        public const int MaxAttemptsCountReached = 8;
        public const int InvalidResponse = 9;
        public const int SigningError = 10;
        // from 1xx to 5xx the same with HTTP codes

        public const int IsCriminalWallet = 1000;
        public const int InvalidSymbol = 1001;
        public const int InvalidPrice = 1002;
        public const int InvalidQty = 1003;
        public const int InvalidWallets = 1004;
        public const int InvalidSigns = 1005;
        public const int InvalidClientId = 1006;
        public const int InvalidOrderId = 1007;
        public const int InvalidTimeStamp = 1008;
        public const int InvalidConnection = 1009;
        public const int InvalidRewardForRedeem = 1010;

        public const int BroadcastError = 1099;
        public const int GetBalanceError = 1100;
        public const int GetTransactionError = 1101;
        public const int GetOutputsError = 1102;
        public const int GetInputError = 1103;
        public const int GetReceiptStatusError = 1106;
        public const int GetRecentBlockHeightError = 1107;
        public const int GetGasPriceError = 1108;
        public const int GetErc20BalanceError = 1109;
        public const int GetErc20TransactionsError = 1110;
        public const int GetInternalTransactionsError = 1111;
        public const int GetTransactionsError = 1112;
        public const int GetBlockNumberError = 1113;
        public const int GetHeaderError = 1114;

        public const int TransactionCreationError = 2000;
        public const int TransactionSigningError = 2001;
        public const int TransactionVerificationError = 2002;
        public const int TransactionBroadcastError = 2003;
        public const int InsufficientFunds = 2004;
        public const int InsufficientGas = 2005;
        public const int InsufficientFee = 2006;
        public const int InsufficientAmount = 2007;
        public const int InsufficientChainFunds = 2008;
        public const int SendingAndReceivingAddressesAreSame = 2009;
        public const int FromAddressIsNullOrEmpty = 2010;

        public const int SwapError = 3000;
        public const int SwapNotFound = 3001;
        public const int WrongSwapMessageOrder = 3002;
        public const int InvalidSecretHash = 3003;
        public const int InvalidPaymentTxId = 3004;
        public const int InvalidSpentPoint = 3005;
        public const int InvalidSwapPaymentTx = 3006;
        public const int InvalidRefundLockTime = 3007;
        public const int InvalidSwapPaymentTxAmount = 3008;
        public const int InvalidRedeemScript = 3009;

        public const int NoLiquidity = 4000;
        public const int PriceHasChanged = 4001;
        public const int TimeoutReached = 4002;
        public const int OrderRejected = 4003;

        public const int WrongDelegationAddress = 5000;
        public const int AlreadyDelegated = 5001;
        public const int EmptyPreApplyOperations = 5002;
        public const int NullTxId = 5003;
        public const int NullOperation = 5004;
        public const int RpcResponseError = 5005;
        public const int AddressNotFound = 5006;
        public const int AutoFillError = 5007;
        public const int OperationBatchingError = 5008;
    }
}