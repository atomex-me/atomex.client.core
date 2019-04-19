using System;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Cryptography;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps
{
    public abstract class CurrencySwap : ICurrencySwap
    {
        public const int DefaultSecretSize = 32; //16;
        public const int DefaultSecretHashSize = 32; //20;

        public const int DefaultInitiatorLockTimeHours = 6;
        public const int DefaultCounterPartyLockTimeHours = 3;
        public const int DefaultGetTransactionAttempts = 5;

        public static TimeSpan DefaultConfirmationCheckInterval = TimeSpan.FromMinutes(1);
        public static TimeSpan DefaultOutputSpentCheckInterval = TimeSpan.FromSeconds(30);
        public static TimeSpan DefaultGetTransactionInterval = TimeSpan.FromSeconds(10);
        public static TimeSpan DefaultRefundInterval = TimeSpan.FromMinutes(1);

        public OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate CounterPartyPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate CounterPartyPaymentSpent { get; set; }

        protected readonly Currency _currency;
        protected readonly SwapState _swapState;
        protected readonly IAccount _account;
        protected readonly ISwapClient _swapClient;
        protected readonly IBackgroundTaskPerformer _taskPerformer;

        protected CurrencySwap(
            Currency currency,
            SwapState swapState,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
        {
            _currency = currency;
            _swapState = swapState ?? throw new ArgumentNullException(nameof(swapState));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _swapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));
            _taskPerformer = taskPerformer ?? throw new ArgumentNullException(nameof(taskPerformer));

            if (_swapState.Order == null)
                throw new ArgumentNullException(nameof(swapState.Order));

            if (_swapState.Requisites == null)
                throw new ArgumentNullException(nameof(swapState.Requisites));
        }

        public abstract Task InitiateSwapAsync();

        public abstract Task AcceptSwapAsync();

        public abstract Task PrepareToReceiveAsync();

        public abstract Task RestoreSwapAsync();

        public abstract Task HandleSwapData(SwapData swapData);

        public abstract Task RedeemAsync();

        public abstract Task BroadcastPaymentAsync();

        protected void SendData(
            SwapDataType dataType,
            byte[] data)
        {
            _swapClient.SendSwapDataAsync(new SwapData
            {
                SwapId = _swapState.Id,
                Symbol = _swapState.Order.Symbol,
                Type = dataType,
                Data = data
            });
        }

        protected void CreateSecret()
        {
            _swapState.Secret = CreateSwapSecret();
        }

        protected void CreateSecretHash()
        {
            _swapState.SecretHash = CreateSwapSecretHash(_swapState.Secret);
        }

        protected void RaiseInitiatorPaymentConfirmed(ISwapState swap)
        {
            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseCounterPartyPaymentConfirmed(ISwapState swap)
        {
            CounterPartyPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseCounterPartyPaymentSpent(ISwapState swap)
        {
            CounterPartyPaymentSpent?.Invoke(this, new SwapEventArgs(swap));
        }

        public static byte[] CreateSwapSecret()
        {
            return Rand.SecureRandomBytes(DefaultSecretSize);
        }

        public static byte[] CreateSwapSecretHash(byte[] secretBytes)
        {
            //return Ripemd160.Compute(Sha256.Compute(secretBytes));
            //return Sha256.Compute(secretBytes);
            return Sha256.Compute(secretBytes, 2);
        }

        public static byte[] CreateSwapSecretHash160(byte[] secretBytes)
        {
            return Ripemd160.Compute(Sha256.Compute(secretBytes));
        }
    }
}