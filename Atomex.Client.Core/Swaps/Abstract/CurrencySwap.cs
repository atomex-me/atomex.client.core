﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Helpers;
using Atomex.Core.Entities;
using Atomex.Cryptography;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Swaps.Abstract
{
    public abstract class CurrencySwap : ICurrencySwap
    {
        public const int DefaultSecretSize = 32;
        public const int DefaultSecretHashSize = 32; //20;

        protected const int DefaultInitiatorLockTimeInSeconds = 7 * 60 * 60; // 7 hours
        protected const int DefaultAcceptorLockTimeInSeconds = 3 * 60 * 60; // 3 hours
        protected const int DefaultGetTransactionAttempts = 10;

        protected static TimeSpan DefaultConfirmationCheckInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan DefaultOutputSpentCheckInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan DefaultGetTransactionInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan DefaultRefundTimeCheckInterval = TimeSpan.FromSeconds(60);
        protected static TimeSpan DefaultMaxSwapTimeout = TimeSpan.FromMinutes(10);
        protected static TimeSpan DefaultForceRefundInterval = TimeSpan.FromMinutes(5);

        public OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate AcceptorPaymentConfirmed { get; set; }
        public OnSwapUpdatedDelegate AcceptorPaymentSpent { get; set; }
        public OnSwapUpdatedDelegate SwapUpdated { get; set; }

        public Currency Currency { get; }
        protected readonly IAccount Account;
        protected readonly ISwapClient SwapClient;

        protected CurrencySwap(
            Currency currency,
            IAccount account,
            ISwapClient swapClient)
        {
            Currency = currency;
            Account = account ?? throw new ArgumentNullException(nameof(account));
            SwapClient = swapClient ?? throw new ArgumentNullException(nameof(swapClient));
        }

        public abstract Task PayAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task PrepareToReceiveAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task WaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task PartyRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RestoreSwapForSoldCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public abstract Task RestoreSwapForPurchasedCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        public virtual Task HandlePartyPaymentAsync(
            ClientSwap swap,
            ClientSwap clientSwap,
            CancellationToken cancellationToken = default)
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

        protected Task TrackTransactionConfirmationAsync(
            ClientSwap swap,
            Currency currency,
            string txId,
            Action<ClientSwap, IBlockchainTransaction, CancellationToken> confirmationHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await currency
                        .IsTransactionConfirmed(
                            txId: txId,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (result.HasError)
                        break;

                    if (result.Value.IsConfirmed)
                    {
                        confirmationHandler?.Invoke(swap, result.Value.Transaction, cancellationToken);
                        break;
                    }

                    await Task.Delay(DefaultConfirmationCheckInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        protected Task ControlRefundTimeAsync(
            ClientSwap swap,
            DateTime refundTimeUtc,
            Action<ClientSwap, CancellationToken> refundTimeReachedHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Log.Debug("Refund time check for swap {@swapId}", swap.Id);

                    var refundTimeReached = DateTime.UtcNow >= refundTimeUtc;

                    if (refundTimeReached)
                    {
                        refundTimeReachedHandler?.Invoke(swap, cancellationToken);
                        break;
                    }

                    await Task.Delay(DefaultRefundTimeCheckInterval, cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
        }
    }
}