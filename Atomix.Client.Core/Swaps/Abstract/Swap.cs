using System;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
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
    public abstract class Swap : ISwap
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

        protected Swap(
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

        public abstract Task RestoreSwapAsync();

        public virtual Task HandleSwapData(SwapData swapData)
        {
            switch (swapData.Type)
            {
                case SwapDataType.SecretHash:
                    return HandleSecretHashAsync(swapData.Data);
                //case SwapDataType.Canceled:
                //    break;
                //case SwapDataType.LockTimeWarning:
                //    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public abstract Task RedeemAsync();

        public abstract Task BroadcastPaymentAsync();

        protected async Task HandleSecretHashAsync(byte[] secretHash)
        {
            Log.Debug(
                messageTemplate: "Handle secret hash {@hash} for swap {@swapId}",
                propertyValue0: secretHash?.ToHexString(),
                propertyValue1: _swapState.Id);

            AcceptSecretHash(secretHash);

            await AcceptSwapAsync()
                .ConfigureAwait(false);
        }

        protected void AcceptSecretHash(byte[] secretHash)
        {
            if (_swapState.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received secret hash message for swap {_swapState.Id}");

            if (secretHash == null || secretHash.Length != DefaultSecretHashSize)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Incorrect secret hash length for swap {_swapState.Id}");

            if (_swapState.SecretHash != null)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Secret hash already received for swap {_swapState.Id}");

            Log.Debug(
                messageTemplate: "Secret hash {@hash} successfully received",
                propertyValue: secretHash.ToHexString());

            _swapState.SecretHash = secretHash;
        }

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

        protected void SendTransactionData(
            SwapDataType dataType,
            IBlockchainTransaction tx)
        {
            SendData(dataType, tx.ToBytes());
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