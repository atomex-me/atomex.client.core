﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Helpers;
using Atomex.Swaps.Tezos.FA2.Helpers;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps.Tezos.FA2
{
    public class FA2Swap : TezosSwap
    {
        private FA2Account FA2Account => _account as FA2Account;
        private TezosAccount TezosAccount { get; }
        private TezosTokens.FA2 FA2 => Currencies.Get<TezosTokens.FA2>(Currency);
        private Atomex.Tezos Xtz => Currencies.Get<Atomex.Tezos>(TezosAccount.Currency);

        public FA2Swap(
            FA2Account account,
            TezosAccount tezosAccount,
            ICurrencies currencies)
            : base(account, currencies)
        {
            TezosAccount = tezosAccount ?? throw new ArgumentNullException(nameof(account));
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var paymentTxs = (await CreatePaymentTxsAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false))
                .ToList();

            if (paymentTxs.Count == 0)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            try
            {
                foreach (var paymentTx in paymentTxs)
                {
                    try
                    {
                        await TezosAccount.AddressLocker
                            .LockAsync(paymentTx.From, cancellationToken)
                            .ConfigureAwait(false);

                        var txsToBroadcast = await CreateApproveTxsAsync(paymentTx, cancellationToken)
                            .ConfigureAwait(false) ?? throw new Exception($"Can't get allowance for {paymentTx.From}");

                        txsToBroadcast.Add(paymentTx);

                        foreach (var tx in txsToBroadcast)
                        {
                            var isInitiateTx = tx.Type.HasFlag(BlockchainTransactionType.SwapPayment);

                            var address = await _account
                                .GetAddressAsync(tx.From, cancellationToken)
                                .ConfigureAwait(false);

                            using var securePublicKey = _account.Wallet
                                .GetPublicKey(FA2, address.KeyIndex);

                            // fill operation
                            var fillResult = await tx
                                .FillOperationsAsync(
                                    securePublicKey: securePublicKey,
                                    headOffset: Atomex.Tezos.HeadOffset,
                                    cancellationToken: cancellationToken)
                                .ConfigureAwait(false);

                            var signResult = await SignTransactionAsync(tx, cancellationToken)
                                .ConfigureAwait(false);

                            if (!signResult)
                            {
                                Log.Error("Transaction signing error.");
                                return;
                            }

                            if (isInitiateTx)
                            {
                                swap.PaymentTx = tx;
                                swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

                                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned, cancellationToken)
                                    .ConfigureAwait(false);
                            }

                            await BroadcastTxAsync(
                                    swap: swap,
                                    tx: tx,
                                    cancellationToken: cancellationToken,
                                    updateBalance: isInitiateTx,
                                    notifyIfUnconfirmed: true,
                                    notifyIfBalanceUpdated: isInitiateTx)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        TezosAccount.AddressLocker.Unlock(paymentTx.From);
                    }

                    swap.PaymentTx = paymentTx;
                    swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap payment error for swap {@swapId}", swap.Id);
                return;
            }
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party payment control for swap {@swap}.", swap.Id);

            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Func<Swap, CancellationToken, Task>(SwapAcceptedHandler)
                : new Func<Swap, CancellationToken, Task>(SwapInitiatedHandler);

            var lockTimeSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            _ = FA2SwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                currency: FA2,
                tezos: Xtz,
                refundTimeStamp: refundTimeUtcInSec,
                interval: ConfirmationCheckInterval,
                initiatedHandler: initiatedHandler,
                canceledHandler: SwapCanceledHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }
        
        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa2 = FA2;

            var secretResult = await FA2SwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: fa2,
                    tezos: Xtz,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!secretResult.HasError && secretResult.Value != null)
            {
                await RedeemConfirmedEventHandler(swap, null, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                swap.RedeemTx != null &&
                swap.RedeemTx.CreationTime != null &&
                swap.RedeemTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                // redeem already broadcast
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: fa2,
                    txId: swap.RedeemTx.Id,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            // check already refunded by initiator
            if (swap.IsAcceptor &&
                swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultInitiatorLockTimeInSeconds) < DateTime.UtcNow)
            {
                var isRefundedByParty = await FA2SwapRefundedHelper
                    .IsRefundedAsync(swap, fa2, Xtz, cancellationToken)
                    .ConfigureAwait(false);

                if (isRefundedByParty != null &&
                    !isRefundedByParty.HasError &&
                    isRefundedByParty.Value)
                {
                    swap.StateFlags |= SwapStateFlags.IsUnsettled;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsUnsettled, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }
            }

            if (swap.IsInitiator)
            {
                var redeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - RedeemTimeReserve;

                if (DateTime.UtcNow > redeemDeadline)
                {
                    Log.Error("Redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for swap {@swapId}", swap.Id);

            var walletAddress = (await FA2Account
                .GetUnspentAddressesAsync(
                    toAddress: swap.ToAddress,
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRedeem,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            var redeemTx = new TezosTransaction
            {
                Currency     = fa2,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = fa2.SwapContractAddress,
                Amount       = 0,
                Fee          = fa2.RedeemFee + fa2.RevealFee,
                GasLimit     = fa2.RedeemGasLimit,
                StorageLimit = fa2.RedeemStorageLimit,
                Params       = RedeemParams(swap),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await TezosAccount.AddressLocker
                    .LockAsync(redeemTx.From, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(FA2, walletAddress.KeyIndex);

                // fill operation
                var fillResult = await redeemTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        headOffset: Atomex.Tezos.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signResult = await SignTransactionAsync(redeemTx, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return;
                }

                swap.RedeemTx = redeemTx;
                swap.StateFlags |= SwapStateFlags.IsRedeemSigned;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned, cancellationToken)
                    .ConfigureAwait(false);

                await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TezosAccount.AddressLocker.Unlock(redeemTx.From);
            }

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: fa2,
                txId: redeemTx.Id,
                confirmationHandler: RedeemConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa2 = FA2;

            if (swap.IsInitiator)
            {
                var partyRedeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - PartyRedeemTimeReserve;

                if (DateTime.UtcNow > partyRedeemDeadline)
                {
                    Log.Error("Party redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for acceptor for swap {@swapId}", swap.Id);

            var walletAddress = (await FA2Account
                .GetUnspentAddressesAsync(
                    toAddress: swap.PartyAddress, // todo: check it
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRedeem,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient balance for party redeem. Cannot find the address containing the required amount of funds.");
                return;
            }

            var redeemTx = new TezosTransaction
            {
                Currency     = fa2,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = fa2.SwapContractAddress,
                Amount       = 0,
                Fee          = fa2.RedeemFee + fa2.RevealFee,
                GasLimit     = fa2.RedeemGasLimit,
                StorageLimit = fa2.RedeemStorageLimit,
                Params       = RedeemParams(swap),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await TezosAccount.AddressLocker
                    .LockAsync(redeemTx.From, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(FA2, walletAddress.KeyIndex);

                // fill operation
                var fillResult = await redeemTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        headOffset: Atomex.Tezos.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);


                var signResult = await SignTransactionAsync(redeemTx, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return;
                }

                await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TezosAccount.AddressLocker.Unlock(redeemTx.From);
            }
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var fa2 = FA2;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(5) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: fa2,
                    txId: swap.RefundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await FA2Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get refund address
                    amount: 0,
                    fee: 0,
                    feePrice: 0,
                    feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                    transactionType: BlockchainTransactionType.SwapRefund,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .FirstOrDefault();

            if (walletAddress == null)
            {
                Log.Error("Insufficient funds for refund");
                return;
            }

            var refundTx = new TezosTransaction
            {
                Currency     = fa2,
                CreationTime = DateTime.UtcNow,
                From         = walletAddress.Address,
                To           = fa2.SwapContractAddress,
                Fee          = fa2.RefundFee + fa2.RevealFee,
                GasLimit     = fa2.RefundGasLimit,
                StorageLimit = fa2.RefundStorageLimit,
                Params       = RefundParams(swap),
                Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund,

                UseRun              = true,
                UseSafeStorageLimit = true,
                UseOfflineCounter   = true
            };

            try
            {
                await TezosAccount.AddressLocker
                    .LockAsync(refundTx.From, cancellationToken)
                    .ConfigureAwait(false);

                using var securePublicKey = _account.Wallet
                    .GetPublicKey(FA2, walletAddress.KeyIndex);

                // fill operation
                var fillResult = await refundTx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        headOffset: Atomex.Tezos.HeadOffset,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var signResult = await SignTransactionAsync(refundTx, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return;
                }

                swap.RefundTx = refundTx;
                swap.StateFlags |= SwapStateFlags.IsRefundSigned;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned, cancellationToken)
                    .ConfigureAwait(false);

                await BroadcastTxAsync(swap, refundTx, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TezosAccount.AddressLocker.Unlock(refundTx.From);
            }

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: fa2,
                txId: refundTx.Id,
                confirmationHandler: RefundConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            _ = FA2SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: FA2,
                tezos: Xtz,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                cancelOnlyIfRefundTimeReached: true,
                redeemedHandler: RedeemCompletedEventHandler,
                canceledHandler: RedeemCanceledEventHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            _ = FA2SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: FA2,
                tezos: Xtz,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                cancelOnlyIfRefundTimeReached: true,
                redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                canceledHandler: RedeemBySomeoneCanceledEventHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        #region Event Handlers

        protected override async Task RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await FA2SwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: FA2,
                        tezos: Xtz,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!isRefundedResult.HasError)
                {
                    if (isRefundedResult.Value)
                    {
                        await RefundConfirmedEventHandler(swap, swap.RefundTx, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await RefundAsync(swap, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in refund time reached handler");
            }
        }

        private async Task RedeemBySomeoneCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemConfirmed, cancellationToken)
                    .ConfigureAwait(false);

                // get transactions & update balance for address async 
                _ = AddressHelper.UpdateAddressBalanceAsync<TezosWalletScanner, TezosAccount>(
                    account: _account,
                    address: swap.ToAddress,
                    cancellationToken: cancellationToken);
            }
        }

        private async Task RedeemBySomeoneCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    var walletAddress = (await _account
                        .GetUnspentAddressesAsync(
                            toAddress: swap.ToAddress,
                            amount: 0,
                            fee: 0,
                            feePrice: 0,
                            feeUsagePolicy: FeeUsagePolicy.EstimatedFee,
                            addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
                            transactionType: BlockchainTransactionType.SwapRedeem,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                        .FirstOrDefault();

                    if (walletAddress == null) //todo: make some panic here
                    {
                        Log.Error(
                            "Counter counterParty redeem need to be made for swap {@swapId}, using secret {@Secret}",
                            swap.Id,
                            Convert.ToBase64String(swap.Secret));

                        return;
                    }

                    await RedeemAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Redeem party control canceled event error");
            }
        }

        public override async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies
                .GetByName(swap.SoldCurrency);

            return await FA2SwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currency: currency,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion Event Handlers

        #region Helpers

        private decimal RequiredAmountInTokens(Swap swap, TezosTokens.FA2 fa2)
        {
            var requiredAmountInTokens = AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price, fa2.DigitsMultiplier);

            // maker network fee
            if (swap.MakerNetworkFee > 0 && swap.MakerNetworkFee < requiredAmountInTokens) // network fee size check
                requiredAmountInTokens += AmountHelper.RoundDown(swap.MakerNetworkFee, fa2.DigitsMultiplier);

            return requiredAmountInTokens;
        }

        protected override async Task<IEnumerable<TezosTransaction>> CreatePaymentTxsAsync(
            Swap swap,
            int lockTimeSeconds,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var fa2 = FA2;

            var requiredAmountInTokens = RequiredAmountInTokens(swap, fa2);
            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            var rewardForRedeemInTokenDigits = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToTokenDigits(fa2.DigitsMultiplier)
                : 0;

            var unspentAddresses = (await FA2Account
                .GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList(new AvailableBalanceAscending());

            unspentAddresses.RemoveRange(0, unspentAddresses.Count - 1);

            var transactions = new List<TezosTransaction>();

            foreach (var walletAddress in unspentAddresses)
            {
                Log.Debug("Create swap payment tx from address {@address} for swap {@swapId}",
                    walletAddress.Address,
                    swap.Id);

                var balanceInTz = (await TezosAccount
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                var balanceInTokens = (await FA2Account
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInTokens);

                var balanceInMtz = balanceInTz.ToMicroTez();

                var isRevealed = await _account
                    .IsRevealedSourceAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var feeAmountInMtz = fa2.ApproveFee +
                    fa2.InitiateFee +
                    (isRevealed ? 0 : fa2.RevealFee);

                var storageLimitInMtz = (fa2.ApproveStorageLimit + fa2.InitiateStorageLimit) * fa2.StorageFeeMultiplier;

                if (balanceInMtz - feeAmountInMtz - storageLimitInMtz - Xtz.MicroTezReserve <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, " +
                        "feeAmount: {@feeAmount}, storageLimit: {@storageLimit}.",
                        walletAddress.Address,
                        balanceInMtz,
                        feeAmountInMtz,
                        storageLimitInMtz);

                    continue;
                }

                var amountInTokens = requiredAmountInTokens > 0
                    ? AmountHelper.DustProofMin(balanceInTokens, requiredAmountInTokens, fa2.DigitsMultiplier, fa2.DustDigitsMultiplier)
                    : 0;

                if (amountInTokens == 0)
                    break;

                if (requiredAmountInTokens > amountInTokens)
                    continue; // insufficient funds

                transactions.Add(new TezosTransaction
                {
                    Currency     = fa2,
                    CreationTime = DateTime.UtcNow,
                    From         = walletAddress.Address,
                    To           = fa2.SwapContractAddress,
                    Fee          = feeAmountInMtz,
                    GasLimit     = fa2.InitiateGasLimit,
                    StorageLimit = fa2.InitiateStorageLimit,
                    Params       = InitParams(swap, fa2.TokenContractAddress, fa2.TokenID, amountInTokens.ToTokenDigits(fa2.DigitsMultiplier), refundTimeStampUtcInSec, (long)rewardForRedeemInTokenDigits),
                    Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment,

                    UseRun              = true,
                    UseSafeStorageLimit = true,
                    UseOfflineCounter   = true
                });
                
                break;
            }

            if (!transactions.Any())
                Log.Warning("Insufficient funds.");

            return transactions;
        }

        private async Task<IList<TezosTransaction>> CreateApproveTxsAsync(
            TezosTransaction paymentTx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await FA2Account
                .GetAddressAsync(paymentTx.From, cancellationToken)
                .ConfigureAwait(false);

            var fa2 = FA2;
            var fa2Api = fa2.BlockchainApi as ITokenBlockchainApi;

            var allowanceResult = await fa2Api
                .TryGetFa2BigMapAllowanceAsync(
                    holderAddress: walletAddress.Address,
                    spenderAddress: fa2.SwapContractAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (allowanceResult.HasError)
            {
                Log.Error("Error while getting token allowance for {@address} with code {@code} and description {@description}",
                    walletAddress.Address,
                    allowanceResult.Error.Code,
                    allowanceResult.Error.Description);

                return null; // todo: maybe add approve 0
            }

            var transactions = new List<TezosTransaction>();

            if (allowanceResult.Value == 0)
            {
                transactions.Add(new TezosTransaction
                {
                    Currency     = fa2,
                    CreationTime = DateTime.UtcNow,
                    From         = walletAddress.Address,
                    To           = fa2.TokenContractAddress,
                    Fee          = fa2.ApproveFee,
                    GasLimit     = fa2.ApproveGasLimit,
                    StorageLimit = fa2.ApproveStorageLimit,
                    Params       = ApproveParams(walletAddress.Address, fa2.SwapContractAddress),
                    Type         = BlockchainTransactionType.TokenApprove,

                    UseRun              = true,
                    UseSafeStorageLimit = true,
                    UseOfflineCounter   = true
                });
            }

            return transactions;
        }

        private async Task<bool> SignTransactionAsync(
            TezosTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await FA2Account
                .GetAddressAsync(
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await FA2Account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            TezosTransaction tx,
            CancellationToken cancellationToken = default,
            bool updateBalance = true,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true)
        {
            var broadcastResult = await Xtz.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new Exception($"Error while broadcast transaction with code {broadcastResult.Error.Code} and description {broadcastResult.Error.Description}");

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            // account new unconfirmed transaction
            await FA2Account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var xtzTx = tx.Clone();
            xtzTx.Currency = Xtz;
            xtzTx.Amount = 0;
            xtzTx.Type = BlockchainTransactionType.Output | (tx.Type.HasFlag(BlockchainTransactionType.TokenApprove)
                ? BlockchainTransactionType.TokenCall
                : BlockchainTransactionType.SwapCall);

            await TezosAccount
                .UpsertTransactionAsync(
                    tx: xtzTx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
        }

        private JObject ApproveParams(
            string owner,
            string spender)
        {
            return JObject.Parse(@"{'entrypoint':'update_operators','value':[{'prim': 'Left','args':[{'prim': 'Pair','args':[{'string': '" + owner + "'},{'string': '" + spender + "'}]}]}]}");
        }

        private JObject InitParams(
            Swap swap,
            string tokenContractAddress,
            long tokenID,
            decimal tokenAmountInDigigts,
            long refundTimestamp,
            long redeemFeeAmount)
        {
            return JObject.Parse(@"{'entrypoint':'initiate','value':{'prim':'Pair','args':[{'prim':'Pair','args':[{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'string':'" + swap.PartyAddress + "'}]},{'prim':'Pair','args':[{'string':'" + refundTimestamp + "'},{'string':'" + tokenContractAddress + "'}]}]},{'prim':'Pair','args':[{'int':'" + tokenID + "'},{'int':'" + tokenAmountInDigigts + "'}]}]}}");
        }

        private JObject RedeemParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'redeem','value':{'bytes':'" + swap.Secret.ToHexString() + "'}}");
        }

        private JObject RefundParams(Swap swap)
        {
            return JObject.Parse(@"{'entrypoint':'refund','value':{'bytes':'" + swap.SecretHash.ToHexString() + "'}}");
        }

        #endregion Helpers
    }
}