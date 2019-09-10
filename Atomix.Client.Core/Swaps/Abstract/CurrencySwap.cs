using System;
using System.Threading.Tasks;
using Atomix.Common.Abstract;
using Atomix.Core.Entities;
using Atomix.Cryptography;
using Atomix.Wallet.Abstract;

namespace Atomix.Swaps.Abstract
{
    public abstract class CurrencySwap : ICurrencySwap
    {
        public const int DefaultSecretSize = 32;
        public const int DefaultSecretHashSize = 32; //20;

        protected const int DefaultInitiatorLockTimeInSeconds = 6 * 60 * 60; // 6 hours
        protected const int DefaultAcceptorLockTimeInSeconds = 3 * 60 * 60; // 3 hours
        protected const int DefaultGetTransactionAttempts = 5;

        protected static TimeSpan DefaultConfirmationCheckInterval = TimeSpan.FromMinutes(1);
        protected static TimeSpan DefaultOutputSpentCheckInterval = TimeSpan.FromSeconds(30);
        protected static TimeSpan DefaultGetTransactionInterval = TimeSpan.FromSeconds(10);
        protected static TimeSpan DefaultRefundInterval = TimeSpan.FromMinutes(1);
        protected static TimeSpan DefaultMaxSwapTimeout = TimeSpan.FromMinutes(10);

        public OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate AcceptorPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate AcceptorPaymentSpent { get; set; }
        public OnSwapUpdatedDelegate SwapUpdated { get; set; }

        public Currency Currency { get; }
        protected readonly IAccount Account;
        protected readonly ISwapClient SwapClient;
        protected readonly IBackgroundTaskPerformer TaskPerformer;

        protected CurrencySwap(
            Currency currency,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
        {
            Currency = currency;
            Account = account ?? throw new ArgumentNullException(nameof(account));
            SwapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));
            TaskPerformer = taskPerformer ?? throw new ArgumentNullException(nameof(taskPerformer));
        }

        public abstract Task BroadcastPaymentAsync(ClientSwap swap);

        public abstract Task PrepareToReceiveAsync(ClientSwap swap);

        public abstract Task RestoreSwapAsync(ClientSwap swap);

        public abstract Task RedeemAsync(ClientSwap swap);

        public abstract Task WaitForRedeemAsync(ClientSwap swap);

        public abstract Task PartyRedeemAsync(ClientSwap swap);

        public virtual Task HandlePartyPaymentAsync(ClientSwap swap, ClientSwap clientSwap)
        {
            return Task.CompletedTask;
        }

        protected void RaiseInitiatorPaymentConfirmed(ClientSwap swap)
        {
            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseAcceptorPaymentConfirmed(ClientSwap swap)
        {
            AcceptorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseAcceptorPaymentSpent(ClientSwap swap)
        {
            AcceptorPaymentSpent?.Invoke(this, new SwapEventArgs(swap));
        }

        protected void RaiseSwapUpdated(ClientSwap swap, SwapStateFlags changedFlag)
        {
            SwapUpdated?.Invoke(this, new SwapEventArgs(swap, changedFlag));
        }

        public static byte[] CreateSwapSecret()
        {
            return Rand.SecureRandomBytes(DefaultSecretSize);
        }

        public static byte[] CreateSwapSecretHash(byte[] secretBytes)
        {
            return Sha256.Compute(secretBytes, 2);
        }

        public static byte[] CreateSwapSecretHash160(byte[] secretBytes)
        {
            return Ripemd160.Compute(Sha256.Compute(secretBytes));
        }
    }
}