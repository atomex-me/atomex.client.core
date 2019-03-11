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
    public abstract class SwapProtocol : ISwapProtocol
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

        public OnSwapUpdatedDelegate SwapUpdated { get; set; }
        public OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate CounterPartyPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate CounterPartyPaymentSpent { get; set; }

        protected readonly Currency _currency;
        protected readonly Swap _swap;
        protected readonly IAccount _account;
        protected readonly ISwapClient _swapClient;
        protected readonly IBackgroundTaskPerformer _taskPerformer;

        protected SwapProtocol(
            Currency currency,
            Swap swap,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            OnSwapUpdatedDelegate onSwapUpdated = null)
        {
            _currency = currency;
            _swap = swap ?? throw new ArgumentNullException(nameof(swap));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _swapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));
            _taskPerformer = taskPerformer ?? throw new ArgumentNullException(nameof(taskPerformer));

            if (onSwapUpdated != null)
                SwapUpdated = onSwapUpdated;
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

        public abstract Task BroadcastPayment();

        protected async Task HandleSecretHashAsync(byte[] secretHash)
        {
            Log.Debug(
                messageTemplate: "Handle secret hash {@hash} for swap {@swapId}",
                propertyValue0: secretHash?.ToHexString(),
                propertyValue1: _swap.Id);

            AcceptSecretHash(secretHash);

            await AcceptSwapAsync()
                .ConfigureAwait(false);
        }

        protected void AcceptSecretHash(byte[] secretHash)
        {
            if (_swap.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received secret hash message for swap {_swap.Id}");

            if (secretHash == null || secretHash.Length != DefaultSecretHashSize)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Incorrect secret hash length for swap {_swap.Id}");

            if (_swap.SecretHash != null)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Secret hash already received for swap {_swap.Id}");

            Log.Debug(
                messageTemplate: "Secret hash {@hash} successfully received",
                propertyValue: secretHash.ToHexString());

            _swap.SetSecretHash(secretHash);
            RaiseSwapUpdated(_swap);
        }

        protected void SendData(
            SwapDataType dataType,
            byte[] data)
        {
            _swapClient.SendSwapDataAsync(new SwapData
            {
                SwapId = _swap.Id,
                Symbol = _swap.Order.Symbol,
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
            _swap.SetSecret(CreateSwapSecret());
            RaiseSwapUpdated(_swap);
        }

        protected void CreateSecretHash()
        {
            _swap.SetSecretHash(CreateSwapSecretHash(_swap.Secret));
            RaiseSwapUpdated(_swap);
        }

        protected void RaiseSwapUpdated(ISwap swap)
        {
            SwapUpdated?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseInitiatorPaymentConfirmed(ISwap swap)
        {
            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseCounterPartyPaymentConfirmed(ISwap swap)
        {
            CounterPartyPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseCounterPartyPaymentSpent(ISwap swap)
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