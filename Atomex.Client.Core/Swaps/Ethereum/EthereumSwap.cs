using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Web3;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
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
        private EthereumConfig_ETH EthConfig => Currencies.Get<EthereumConfig_ETH>(Currency);
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

            var paymentTx = await CreatePaymentTxAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (paymentTx == null)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            try
            {
                try
                {
                    await EthereumAccount.AddressLocker
                        .LockAsync(paymentTx.From, cancellationToken)
                        .ConfigureAwait(false);

                    var nonceResult = await EthereumNonceManager.Instance
                        .GetNonceAsync(EthConfig, paymentTx.From)
                        .ConfigureAwait(false);

                    if (nonceResult.HasError)
                    {
                        Log.Error("Nonce getting error with code {@code} and description {@description}",
                            nonceResult.Error.Code,
                            nonceResult.Error.Description);

                        return;
                    }

                    paymentTx.Nonce = nonceResult.Value;

                    var signResult = await SignTransactionAsync(paymentTx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    swap.PaymentTx = paymentTx;
                    swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

                    await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned, cancellationToken)
                        .ConfigureAwait(false);

                    await BroadcastTxAsync(swap, paymentTx, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    EthereumAccount.AddressLocker.Unlock(paymentTx.From);
                }

                swap.PaymentTx = paymentTx;
                swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                    .ConfigureAwait(false);

                // check initiate payment tx confirmation
                var isInitiated = await WaitPaymentConfirmationAsync(paymentTx.Id, InitiationTimeout, cancellationToken)
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

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            _ = EthereumSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                currency: EthConfig,
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
            var ethConfig = EthConfig;

            var secretResult = await EthereumSwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: ethConfig,
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
                swap.RedeemTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(30) > DateTime.UtcNow)
            {
                // redeem already broadcast
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: ethConfig,
                    dataRepository: _account.DataRepository,
                    txId: swap.RedeemTx.Id,
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

            var gasPrice = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = await _account
                .GetAddressAsync(swap.RedeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem from local db", swap.RedeemFromAddress);
                return;
            }

            var feeInEth = ethConfig.GetFeeAmount(ethConfig.RedeemGasLimit, gasPrice);

            if (walletAddress.Balance < feeInEth)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            EthereumTransaction_OLD redeemTx;

            try
            {
                await EthereumAccount.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(ethConfig, walletAddress.Address, pending: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                {
                    Log.Error("Nonce getting error with code {@code} and description {@description}",
                        nonceResult.Error.Code,
                        nonceResult.Error.Description);

                    return;
                }

                var message = new RedeemFunctionMessage
                {
                    FromAddress  = walletAddress.Address,
                    HashedSecret = swap.SecretHash,
                    Secret       = swap.Secret,
                    Nonce        = nonceResult.Value,
                    GasPrice     = EthereumConfig_ETH.GweiToWei(gasPrice),
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(ethConfig.RedeemGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

                redeemTx = new EthereumTransaction_OLD(ethConfig.Name, txInput)
                {
                    Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
                };

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
            catch
            {
                throw;
            }
            finally
            {
                EthereumAccount.AddressLocker.Unlock(walletAddress.Address);
            }

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: ethConfig,
                dataRepository: _account.DataRepository,
                txId: redeemTx.Id,
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

            var gasPrice = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem for party from local db", swap.FromAddress);
                return;
            }

            var feeInEth = ethConfig.GetFeeAmount(ethConfig.RedeemGasLimit, gasPrice);

            if (walletAddress.Balance < feeInEth)
            {
                Log.Error("Insufficient funds for redeem for party");
                return;
            }

            using var addressLock = await EthereumAccount.AddressLocker
                .GetLockAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(ethConfig, walletAddress.Address, pending: true, cancellationToken)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error("Nonce getting error with code {@code} and description {@description}",
                    nonceResult.Error.Code,
                    nonceResult.Error.Description);

                return;
            }

            var message = new RedeemFunctionMessage
            {
                FromAddress  = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                Secret       = swap.Secret,
                Nonce        = nonceResult.Value,
                GasPrice     = EthereumConfig_ETH.GweiToWei(gasPrice),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(ethConfig.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

            var redeemTx = new EthereumTransaction_OLD(ethConfig.Name, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRedeem
            };

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

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var ethConfig = EthConfig;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(20) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: ethConfig,
                    dataRepository: _account.DataRepository,
                    txId: swap.RefundTx.Id,
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
                var txResult = await EthereumSwapInitiatedHelper
                    .TryToFindPaymentAsync(swap, ethConfig, cancellationToken)
                    .ConfigureAwait(false);

                if (!txResult.HasError && txResult.Value == null)
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

            var gasPrice = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for refund from local db", swap.FromAddress);
                return;
            }

            var feeInEth = ethConfig.GetFeeAmount(ethConfig.RefundGasLimit, gasPrice);

            if (walletAddress.Balance < feeInEth)
            {
                Log.Error("Insufficient funds for refund");
                return;
            }

            EthereumTransaction_OLD refundTx;

            try
            {
                await EthereumAccount.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(ethConfig, walletAddress.Address, pending: true, cancellationToken)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                {
                    Log.Error("Nonce getting error with code {@code} and description {@description}",
                        nonceResult.Error.Code,
                        nonceResult.Error.Description);

                    return;
                }

                var message = new RefundFunctionMessage
                {
                    FromAddress  = walletAddress.Address,
                    HashedSecret = swap.SecretHash,
                    GasPrice     = EthereumConfig_ETH.GweiToWei(gasPrice),
                    Nonce        = nonceResult.Value,
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(ethConfig.RefundGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

                refundTx = new EthereumTransaction_OLD(ethConfig.Name, txInput)
                {
                    Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapRefund
                };

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
            catch
            {
                throw;
            }
            finally
            {
                EthereumAccount.AddressLocker.Unlock(walletAddress.Address);
            }

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: ethConfig,
                dataRepository: _account.DataRepository,
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
            _ = EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: EthConfig,
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
            _ = EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: EthConfig,
                refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                interval: TimeSpan.FromSeconds(30),
                cancelOnlyIfRefundTimeReached: true,
                redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                canceledHandler: RedeemBySomeoneCanceledEventHandler,
                cancellationToken: cancellationToken);

            return Task.CompletedTask;
        }

        public override async Task<Result<IBlockchainTransaction_OLD>> TryToFindPaymentAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var currency = Currencies
                .GetByName(swap.SoldCurrency);

            return await EthereumSwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    currency: currency,
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
                var isRefundedResult = await EthereumSwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: EthConfig,
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
                _ = AddressHelper.UpdateAddressBalanceAsync<EthereumWalletScanner, EthereumAccount>(
                    account: _account,
                    address: swap.ToAddress,
                    cancellationToken: cancellationToken);
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

        protected virtual async Task<EthereumTransaction_OLD> CreatePaymentTxAsync(
            Swap swap,
            int lockTimeInSeconds,
            CancellationToken cancellationToken = default)
        {
            var ethConfig = EthConfig;

            Log.Debug("Create payment transaction from address {@address} for swap {@swapId}", swap.FromAddress, swap.Id);

            var requiredAmountInEth = AmountHelper.QtyToSellAmount(swap.Side, swap.Qty, swap.Price, ethConfig.DigitsMultiplier);

            // maker network fee
            if (swap.MakerNetworkFee > 0 && swap.MakerNetworkFee < requiredAmountInEth) // network fee size check
                requiredAmountInEth += AmountHelper.RoundDown(swap.MakerNetworkFee, ethConfig.DigitsMultiplier);

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            var rewardForRedeemInEth = swap.PartyRewardForRedeem;

            var walletAddress = await _account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            var gasPrice = await ethConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var balanceInEth = walletAddress.Balance;

            Log.Debug("Available balance: {@balance}", balanceInEth);

            var feeAmountInEth = rewardForRedeemInEth == 0
                ? ethConfig.InitiateFeeAmount(gasPrice)
                : ethConfig.InitiateWithRewardFeeAmount(gasPrice);

            if (balanceInEth < feeAmountInEth + requiredAmountInEth)
            {
                Log.Warning(
                    "Insufficient funds at {@address}. Balance: {@balance}, required: {@required}, " +
                    "feeAmount: {@feeAmount}, missing: {@result}.",
                    walletAddress.Address,
                    balanceInEth,
                    requiredAmountInEth,
                    feeAmountInEth,
                    balanceInEth - feeAmountInEth - requiredAmountInEth);

                return null;
            }

            var nonceResult = await ((IEthereumBlockchainApi_OLD)ethConfig.BlockchainApi)
                .GetTransactionCountAsync(walletAddress.Address, pending: false, cancellationToken)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error($"Getting nonce error: {nonceResult.Error.Description}");
                return null;
            }
                    
            TransactionInput txInput;

            var message = new InitiateFunctionMessage
            {
                HashedSecret    = swap.SecretHash,
                Participant     = swap.PartyAddress,
                RefundTimestamp = refundTimeStampUtcInSec,
                AmountToSend    = EthereumConfig_ETH.EthToWei(requiredAmountInEth),
                FromAddress     = walletAddress.Address,
                GasPrice        = EthereumConfig_ETH.GweiToWei(gasPrice),
                Nonce           = nonceResult.Value,
                RedeemFee       = EthereumConfig_ETH.EthToWei(rewardForRedeemInEth)
            };

            var initiateGasLimit = rewardForRedeemInEth == 0
                ? ethConfig.InitiateGasLimit
                : ethConfig.InitiateWithRewardGasLimit;

            message.Gas = await EstimateGasAsync(message, new BigInteger(initiateGasLimit))
                .ConfigureAwait(false);

            txInput = message.CreateTransactionInput(ethConfig.SwapContractAddress);

            return new EthereumTransaction_OLD(ethConfig.Name, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
            };
        }

        private async Task<bool> SignTransactionAsync(
            EthereumTransaction_OLD tx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await _account
                .GetAddressAsync(
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await _account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    currency: EthConfig,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            EthereumTransaction_OLD tx,
            CancellationToken cancellationToken = default)
        {
            var broadcastResult = await EthConfig.BlockchainApi
                .TryBroadcastAsync(tx, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new InternalException(broadcastResult.Error);

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            // account new unconfirmed transaction
            await _account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: true,
                    notifyIfUnconfirmed: true,
                    notifyIfBalanceUpdated: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<BigInteger> EstimateGasAsync<TMessage>(
            TMessage message,
            BigInteger defaultGas) where TMessage : FunctionMessage, new()
        {
            try
            {
                var web3 = new Web3(UriByChain(EthConfig.Chain));
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

        // todo: use etherscan instead
        public static string UriByChain(Chain chain)
        {
            return chain switch
            {
                Chain.MainNet => "https://mainnet.infura.io/v3/df01d4ef450640a2a48d9af4c2078eaf",
                Chain.Ropsten => "https://rinkeby.infura.io/v3/df01d4ef450640a2a48d9af4c2078eaf",
                Chain.Rinkeby => "https://ropsten.infura.io/v3/df01d4ef450640a2a48d9af4c2078eaf",
                _ => null,
            };
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
                    .DataRepository
                    .GetTransactionByIdAsync(EthConfig.Name, txId, EthConfig.TransactionType)
                    .ConfigureAwait(false);

                if (tx is not { IsConfirmed: true }) continue;

                return tx.IsConfirmed;
            }

            return false;
        }

        #endregion Helpers
    }
}