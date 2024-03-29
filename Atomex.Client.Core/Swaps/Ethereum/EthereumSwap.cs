﻿using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Messages.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Ethereum.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Ethereum;

namespace Atomex.Swaps.Ethereum
{
    public class EthereumSwap : CurrencySwap
    {
        public const int MaxRedeemCheckAttempts = 2;
        public const int MaxRefundCheckAttempts = 2;
        public const int RedeemCheckAttemptIntervalInSec = 5;
        public const int RefundCheckAttemptIntervalInSec = 5;
        public static TimeSpan InitiationTimeout = TimeSpan.FromMinutes(20);
        public static TimeSpan InitiationCheckInterval = TimeSpan.FromSeconds(30);
        private EthereumConfig EthConfig => Currencies.Get<EthereumConfig>(Currency);
        protected readonly EthereumAccount _account;

        public EthereumSwap(
            EthereumAccount account,
            ICurrencies currencies)
            : base(account.Currency, currencies)
        {
            _account = account ?? throw new ArgumentNullException(nameof(account));
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

            var txRequest = await CreatePaymentTxAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (txRequest == null)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            string txId = null;

            try
            {
                try
                {
                    await EthereumAccount.AddressLocker
                        .LockAsync(txRequest.From, cancellationToken)
                        .ConfigureAwait(false);

                    var (nonce, error) = await EthereumNonceManager.Instance
                        .GetNonceAsync(EthConfig.GetEthereumApi(), txRequest.From)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("Nonce getting error with code {@code} and message {@message}",
                            error.Value.Code,
                            error.Value.Message);

                        return;
                    }

                    txRequest.Nonce = nonce;

                    var signResult = await _account
                        .SignAsync(txRequest, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    //swap.PaymentTx = txRequest;
                    swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned, cancellationToken)
                        .ConfigureAwait(false);

                    txId = await BroadcastTxAsync(
                            swap,
                            txRequest,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    EthereumAccount.AddressLocker.Unlock(txRequest.From);
                }

                //swap.PaymentTx = txRequest;
                swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                    .ConfigureAwait(false);

                // check initiate payment tx confirmation
                var isInitiated = await WaitPaymentConfirmationAsync(txId, InitiationTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (!isInitiated)
                {
                    Log.Error("Initiation payment tx not confirmed after timeout {@timeout}", InitiationTimeout.Minutes);
                    return;
                }
                
                swap.StateFlags |= SwapStateFlags.IsPaymentConfirmed;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentConfirmed, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap payment error for swap {@swapId}", swap.Id);
            }
        }

        public override Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start party {@currency} payment control for swap {@swap}", Currency, swap.Id);

            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Func<Swap, CancellationToken, Task>(SwapAcceptedHandler)
                : new Func<Swap, CancellationToken, Task>(SwapInitiatedHandler);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            _ = Task.Run(() => EthereumSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                ethereumConfig: EthConfig,
                refundTimeStamp: refundTimeUtcInSec,
                interval: ConfirmationCheckInterval,
                initiatedHandler: initiatedHandler,
                canceledHandler: SwapCanceledHandler,
                cancellationToken: cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var ethConfig = EthConfig;

            var (secret, error) = await EthereumSwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: ethConfig,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error == null && secret != null)
            {
                await RedeemConfirmedEventHandler(swap, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                swap.LastRedeemTryTimeStamp + TimeSpan.FromMinutes(30) > DateTime.UtcNow)
            {
                // redeem already broadcast
                _ = TrackTransactionConfirmationAsync<EthereumTransaction>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
                    txId: swap.RedeemTxId,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
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

            var (gasPrice, gasPriceError) = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return;
            }

            var walletAddress = await _account
                .GetAddressAsync(swap.RedeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem from local db", swap.RedeemFromAddress);
                return;
            }

            var feeInWei = ethConfig.RedeemGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

            if (walletAddress.Balance < feeInWei)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            string txId = null;

            try
            {
                await EthereumAccount.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var (nonce, nonceError) = await EthereumNonceManager.Instance
                    .GetNonceAsync(
                        api: EthConfig.GetEthereumApi(),
                        address: walletAddress.Address,
                        pending: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (nonceError != null)
                {
                    Log.Error("Nonce getting error with code {@code} and message {@message}",
                        nonceError.Value.Code,
                        nonceError.Value.Message);

                    return;
                }

                var message = new RedeemMessage
                {
                    FromAddress          = walletAddress.Address,
                    HashedSecret         = swap.SecretHash,
                    Secret               = swap.Secret,
                    Nonce                = nonce,
                    MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                    MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                    TransactionType      = EthereumHelper.Eip1559TransactionType
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(ethConfig.RedeemGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

                var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

                var signResult = await _account
                    .SignAsync(txRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return;
                }

                swap.StateFlags |= SwapStateFlags.IsRedeemSigned;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemSigned, cancellationToken)
                    .ConfigureAwait(false);

                txId = await BroadcastTxAsync(
                        swap,
                        txRequest,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                throw;
            }
            finally
            {
                EthereumAccount.AddressLocker.Unlock(walletAddress.Address);
            }

            swap.RedeemTxId = txId;
            swap.LastRedeemTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<EthereumTransaction>(
                swap: swap,
                localStorage: _account.LocalStorage,
                txId: txId,
                confirmationHandler: RedeemConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.IsInitiator)
            {
                var partyRedeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - PartyRedeemTimeReserve;

                if (DateTime.UtcNow > partyRedeemDeadline)
                {
                    Log.Error("Party redeem deadline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for counterParty for swap {@swapId}", swap.Id);

            var ethConfig = EthConfig;

            var (gasPrice, gasPriceError) = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return;
            }

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem for party from local db", swap.FromAddress);
                return;
            }

            var feeInWei = ethConfig.RedeemGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

            if (walletAddress.Balance < feeInWei)
            {
                Log.Error("Insufficient funds for redeem for party");
                return;
            }

            using var addressLock = await EthereumAccount.AddressLocker
                .GetLockAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var (nonce, error) = await EthereumNonceManager.Instance
                .GetNonceAsync(
                    api: EthConfig.GetEthereumApi(),
                    address: walletAddress.Address,
                    pending: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error("Nonce getting error with code {@code} and message {@message}",
                    error.Value.Code,
                    error.Value.Message);

                return;
            }

            var message = new RedeemMessage
            {
                FromAddress          = walletAddress.Address,
                HashedSecret         = swap.SecretHash,
                Secret               = swap.Secret,
                Nonce                = nonce,
                MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                TransactionType      = EthereumHelper.Eip1559TransactionType
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(ethConfig.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

            var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

            var signResult = await _account
                .SignAsync(txRequest, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            var _ = await BroadcastTxAsync(
                    swap,
                    txRequest,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var ethConfig = EthConfig;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.LastRefundTryTimeStamp + TimeSpan.FromMinutes(20) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync<EthereumTransaction>(
                    swap: swap,
                    localStorage: _account.LocalStorage,
                    txId: swap.RefundTxId,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken);

                return;
            }

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var lockTime = swap.TimeStamp.ToUniversalTime() + TimeSpan.FromSeconds(lockTimeInSeconds);

            await RefundTimeDelayAsync(lockTime, cancellationToken)
                .ConfigureAwait(false);

            // check swap initiation
            try
            {
                var (tx, error) = await EthereumSwapInitiatedHelper
                    .TryToFindPaymentAsync(swap, ethConfig, cancellationToken)
                    .ConfigureAwait(false);

                if (error == null && tx == null)
                {
                    // swap not initiated and must be canceled
                    swap.StateFlags |= SwapStateFlags.IsCanceled;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsCanceled, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, $"Can't check {swap.Id} swap initiation for ETH");
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var (gasPrice, gasPriceError) = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return;
            }

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for refund from local db", swap.FromAddress);
                return;
            }

            var feeInWei = ethConfig.RefundGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

            if (walletAddress.Balance < feeInWei)
            {
                Log.Error("Insufficient funds for refund");
                return;
            }

            string txId = null;

            try
            {
                await EthereumAccount.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var (nonce, error) = await EthereumNonceManager.Instance
                    .GetNonceAsync(
                        api: EthConfig.GetEthereumApi(),
                        address: walletAddress.Address,
                        pending: true,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("Nonce getting error with code {@code} and message {@message}",
                        error.Value.Code,
                        error.Value.Message);

                    return;
                }

                var message = new RefundMessage
                {
                    FromAddress          = walletAddress.Address,
                    HashedSecret         = swap.SecretHash,
                    MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                    MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                    Nonce                = nonce,
                    TransactionType      = EthereumHelper.Eip1559TransactionType
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(ethConfig.RefundGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

                var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

                var signResult = await _account
                    .SignAsync(txRequest, cancellationToken)
                    .ConfigureAwait(false);

                if (!signResult)
                {
                    Log.Error("Transaction signing error");
                    return;
                }

                swap.StateFlags |= SwapStateFlags.IsRefundSigned;

                await UpdateSwapAsync(swap, SwapStateFlags.IsRefundSigned, cancellationToken)
                    .ConfigureAwait(false);

                txId = await BroadcastTxAsync(
                        swap,
                        txRequest,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                throw;
            }
            finally
            {
                EthereumAccount.AddressLocker.Unlock(walletAddress.Address);
            }

            swap.RefundTxId = txId;
            swap.LastRefundTryTimeStamp = DateTime.UtcNow;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync<EthereumTransaction>(
                swap: swap,
                localStorage: _account.LocalStorage,
                txId: txId,
                confirmationHandler: RefundConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitingForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Start waiting for {@currency} redeem for swap {@swap}", Currency, swap.Id);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            _ = Task.Run(() => EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: EthConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                redeemedHandler: RedeemCompletedEventHandler,
                canceledHandler: RedeemCanceledEventHandler,
                cancellationToken: cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            _ = Task.Run(() => EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: EthConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                canceledHandler: RedeemBySomeoneCanceledEventHandler,
                cancellationToken: cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            return await EthereumSwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currencyConfig: EthConfig,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #region Event Handlers

        protected override async Task RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var (isRefunded, error) = await EthereumSwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: EthConfig,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error == null)
                {
                    if (isRefunded)
                    {
                        await RefundConfirmedEventHandler(swap, cancellationToken)
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
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _account
                            .UpdateBalanceAsync(swap.ToAddress, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Debug($"Ethereum swap update balance for address {swap.ToAddress} canceled");
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, $"Error while update balance for address {swap.ToAddress}");
                    }
                });
            }
        }

        private async Task RedeemBySomeoneCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken)
        {
            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    await RedeemAsync(swap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Redeem party control canceled event error");
            }
        }

        #endregion Event Handlers

        #region Helpers

        public static decimal GetRequiredAmount(Swap swap, EthereumConfig eth)
        {
            var requiredAmountInEth = AmountHelper.QtyToSellAmount(swap.Side, swap.Qty, swap.Price, eth.Precision);

            // maker network fee
            if (swap.MakerNetworkFee > 0 && swap.MakerNetworkFee < requiredAmountInEth) // network fee size check
                requiredAmountInEth += AmountHelper.RoundDown(swap.MakerNetworkFee, eth.Precision);

            return requiredAmountInEth;
        }

        protected virtual async Task<EthereumTransactionRequest> CreatePaymentTxAsync(
            Swap swap,
            int lockTimeInSeconds,
            CancellationToken cancellationToken = default)
        {
            var ethConfig = EthConfig;

            Log.Debug("Create payment transaction from address {@address} for swap {@swapId}", swap.FromAddress, swap.Id);

            var requiredAmountInEth = GetRequiredAmount(swap, ethConfig);
            var requiredAmountInWei = requiredAmountInEth.EthToWei();

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            var rewardForRedeemInEth = swap.PartyRewardForRedeem;

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            var (gasPrice, gasPriceError) = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return null;
            }

            var balanceInWei = walletAddress.Balance;

            Log.Debug("Available balance: {@balance}", balanceInWei);

            var feeAmountInWei = rewardForRedeemInEth == 0
                ? ethConfig.InitiateGasLimit * gasPrice.MaxFeePerGas.GweiToWei()
                : ethConfig.InitiateWithRewardGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

            if (balanceInWei < feeAmountInWei + requiredAmountInWei)
            {
                Log.Warning(
                    "Insufficient funds at {@address}. Balance: {@balance}, required: {@required}, " +
                    "feeAmount: {@feeAmount}, missing: {@result}.",
                    walletAddress.Address,
                    balanceInWei,
                    requiredAmountInWei,
                    feeAmountInWei,
                    balanceInWei - feeAmountInWei - requiredAmountInWei);

                return null;
            }

            var (nonce, error) = await EthConfig
                .GetEthereumApi()
                .GetTransactionsCountAsync(
                    address: walletAddress.Address,
                    pending: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
            {
                Log.Error($"Getting nonce error: {error.Value.Message}");
                return null;
            }
                    
            TransactionInput txInput;

            var message = new InitiateMessage
            {
                HashedSecret         = swap.SecretHash,
                Participant          = swap.PartyAddress,
                RefundTimestamp      = refundTimeStampUtcInSec,
                AmountToSend         = requiredAmountInWei,
                FromAddress          = walletAddress.Address,
                MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                Nonce                = nonce,
                RedeemFee            = rewardForRedeemInEth.EthToWei(),
                TransactionType      = EthereumHelper.Eip1559TransactionType
            };

            var initiateGasLimit = rewardForRedeemInEth == 0
                ? ethConfig.InitiateGasLimit
                : ethConfig.InitiateWithRewardGasLimit;

            message.Gas = await EstimateGasAsync(message, new BigInteger(initiateGasLimit))
                .ConfigureAwait(false);

            txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

            return new EthereumTransactionRequest(txInput, EthConfig.ChainId);
        }

        private async Task<string> BroadcastTxAsync(
            Swap swap,
            EthereumTransactionRequest txRequest,
            CancellationToken cancellationToken = default)
        {
            var api = EthConfig.GetEtherScanApi();

            var (txId, error) = await api
                .BroadcastAsync(txRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                throw new InternalException(error.Value);

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            var tx = new EthereumTransaction(txRequest, txId);

            await _account
                .LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txId;
        }

        private async Task<BigInteger> EstimateGasAsync<TMessage>(
            TMessage message,
            BigInteger defaultGas) where TMessage : FunctionMessage, new()
        {
            try
            {
                var web3 = new Web3(EthConfig.InfuraApi);
                var txHandler = web3.Eth.GetContractTransactionHandler<TMessage>();

                var estimatedGas = await txHandler
                    .EstimateGasAsync(EthConfig.SwapContractAddress, message)
                    .ConfigureAwait(false);

                Log.Debug("Estimated gas {@gas}", estimatedGas?.Value.ToString());

                var estimatedValue = estimatedGas?.Value ?? defaultGas;

                return defaultGas / estimatedValue >= 2
                    ? defaultGas
                    : estimatedValue;
            }
            catch (Exception)
            {
                Log.Debug("Error while estimating fee");
            }

            return defaultGas;
        }

        private async Task<bool> WaitPaymentConfirmationAsync(
            string txId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var timeStamp = DateTime.UtcNow;

            while (DateTime.UtcNow < timeStamp + timeout)
            {
                await Task.Delay(InitiationCheckInterval, cancellationToken)
                    .ConfigureAwait(false);
                
                var tx = await _account
                    .LocalStorage
                    .GetTransactionByIdAsync<EthereumTransaction>(EthConfig.Name, txId)
                    .ConfigureAwait(false);

                if (!tx.IsConfirmed)
                    continue;

                return true;
            }

            return false;
        }

        #endregion Helpers
    }
}