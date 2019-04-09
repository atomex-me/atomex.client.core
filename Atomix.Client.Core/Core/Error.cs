namespace Atomix.Core
{
    public class Error
    {
        public int Code { get; set; }
        public string Description { get; set; }

        public Error()
            : this(0, null)
        {      
        }

        public Error(int code, string description)
        {
            Code = code;
            Description = description;
        }

        public override string ToString()
        {
            return $"{{Code: {Code}, Description: {Description}}}";
        }
    }

    public class Errors
    {
        public const int NoError = 0;
        public const int NotAuthorized = 1;
        public const int AuthError = 2;

        public const int NotSupported = 10000;
        //public const int InvalidJson = 10001;      
        public const int InvalidSymbol = 10002;
        public const int InvalidSide = 10003;
        public const int InvalidPrice = 10004;
        public const int InvalidQty = 10005;
        public const int InvalidWallets = 10006;
        public const int InvalidSigns = 10007;
        public const int InvalidClientId = 10008;
        public const int InvalidOrderId = 10009;
        public const int InvalidTimeStamp = 10010;
        public const int InvalidUserId = 10011;
        public const int InvalidUserName = 10012;
        public const int InvalidPassword = 10013;
        public const int InvalidCredentials = 10014;
        public const int InvalidConnection = 10015;
        public const int InsufficientFunds = 10100;
        public const int InsufficientGas = 10101;
        public const int InsufficientFee = 10102;

        // swap errors
        public const int SwapNotFound = 20000;
        public const int WrongSwapData = 20001;
        public const int WrongSwapMessageOrder = 20002;
        public const int UnhandledSwapDataType = 20003;

        public const int TransactionCreationError = 20004;
        public const int TransactionSigningError = 20005;
        public const int TransactionVerificationError = 20006;

        public const int InvalidSecretHash = 20050;
        public const int InvalidSpentPoint = 20051;
        public const int SwapError = 20052;

    }
}