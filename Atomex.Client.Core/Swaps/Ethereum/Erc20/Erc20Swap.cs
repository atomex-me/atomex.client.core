using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;

using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;

using Atomex.Abstract;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20.Messages;
using Atomex.Blockchain.Ethereum.Erc20.Messages.Swaps.V1;
using Atomex.Blockchain.Ethereum.Messages.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Ethereum.Erc20.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;

namespace Atomex.Swaps.Ethereum
{
    public class Erc20Swap : CurrencySwap
    {
        private Erc20Account Erc20Account { get; }
        private EthereumAccount EthereumAccount { get; }
        private Erc20Config Erc20Config => Currencies.Get<Erc20Config>(Currency);
        private EthereumConfig EthConfig => Currencies.Get<EthereumConfig>(EthereumAccount.Currency);

        public Erc20Swap(
            Erc20Account account,
            EthereumAccount ethereumAccount,
            ICurrencies currencies)
            : base(account.Erc20Config.Name, currencies)
        {
            Erc20Account = account ?? throw new ArgumentNullException(nameof(account));
            EthereumAccount = ethereumAccount ?? throw new ArgumentNullException(nameof(ethereumAccount));
        }

        public override async Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            if (!await CheckPayRelevanceAsync(swap, cancellationToken))
                return;

            var paymentTxRequest = await CreatePaymentTxAsync(swap, cancellationToken)
                .ConfigureAwait(false);

            if (paymentTxRequest == null)
            {
                Log.Error("Can't create payment transaction");
                return;
            }

            string paymentTxId = null;

            try
            {
                try
                {
                    await EthereumAccount.AddressLocker
                        .LockAsync(paymentTxRequest.From, cancellationToken)
                        .ConfigureAwait(false);

                    var approveTxRequests = await CreateApproveTxsAsync(swap, paymentTxRequest, cancellationToken)
                        .ConfigureAwait(false) ?? throw new Exception($"Can't get allowance for {paymentTxRequest.From}");

                    var txRequests = approveTxRequests
                        .Select(tx => (tx, TransactionType.Output | TransactionType.TokenApprove | TransactionType.ContractCall))
                        .ToList()
                        .AddEx((paymentTxRequest, TransactionType.Output | TransactionType.SwapPayment | TransactionType.ContractCall));

                    foreach (var (txRequest, type) in txRequests)
                    {
                        var isPaymentTx = type.HasFlag(TransactionType.SwapPayment);

                        var (nonce, error) = await EthereumNonceManager.Instance
                            .GetNonceAsync(EthConfig.GetEthereumApi(), txRequest.From, pending: true, cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("Nonce getting error with code {@code} and message {@message}",
                                error.Value.Code,
                                error.Value.Message);

                            return;
                        }

                        txRequest.Nonce = nonce;

                        var signResult = await EthereumAccount
                            .SignAsync(txRequest, cancellationToken)
                            .ConfigureAwait(false);

                        if (!signResult)
                        {
                            Log.Error("Transaction signing error");
                            return;
                        }

                        if (isPaymentTx)
                        {
                            swap.StateFlags |= SwapStateFlags.IsPaymentSigned;

                            await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentSigned, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        var txId = await BroadcastTxAsync(
                                swap: swap,
                                txRequest: txRequest,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (isPaymentTx)
                            paymentTxId = txId;
                    }
                }
                catch
                {
                    throw;
                }
                finally
                {
                    EthereumAccount.AddressLocker.Unlock(paymentTxRequest.From);
                }

                //swap.PaymentTx = paymentTxRequest;
                swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                    .ConfigureAwait(false);

                var isInitiateConfirmed = await WaitPaymentConfirmationAsync(
                        txId: paymentTxId,
                        timeout: EthereumSwap.InitiationTimeout,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!isInitiateConfirmed)
                {
                    Log.Error("Initiation payment tx not confirmed after timeout {@timeout}", EthereumSwap.InitiationTimeout.Minutes);
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

            _ = Task.Run(() => Erc20SwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                erc20Config: Erc20Config,
                lockTimeInSec: lockTimeInSeconds,
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
            var erc20Config = Erc20Config;

            var (secret, error) = await Erc20SwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    erc20Config: erc20Config,
                    attempts: EthereumSwap.MaxRedeemCheckAttempts,
                    attemptIntervalInSec: EthereumSwap.RedeemCheckAttemptIntervalInSec,
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
                    localStorage: Erc20Account.LocalStorage,
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

            var (gasPrice, gasPriceError) = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return;
            }

            var walletAddress = await EthereumAccount
                .GetAddressAsync(swap.RedeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem from local db", swap.RedeemFromAddress);
                return;
            }

            var feeInWei = erc20Config.RedeemGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

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
                    .GetNonceAsync(EthConfig.GetEthereumApi(), walletAddress.Address, pending: true, cancellationToken)
                    .ConfigureAwait(false);

                if (nonceError != null)
                {
                    Log.Error("Nonce getting error with code {@code} and message {@message}",
                        nonceError.Value.Code,
                        nonceError.Value.Message);

                    return;
                }

                var message = new Erc20RedeemMessage
                {
                    FromAddress          = walletAddress.Address,
                    HashedSecret         = swap.SecretHash,
                    Secret               = swap.Secret,
                    Nonce                = nonce,
                    MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                    MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                    TransactionType      = EthereumHelper.Eip1559TransactionType,
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.RedeemGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(erc20Config.SwapContractAddress);

                var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

                var signResult = await EthereumAccount
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
                localStorage: Erc20Account.LocalStorage,
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

            var erc20Config = Erc20Config;

            var (gasPrice, gasPriceError) = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return;
            }

            var walletAddress = await EthereumAccount
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem for party from local db", swap.FromAddress);
                return;
            }

            var feeInWei = erc20Config.RedeemGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

            if (walletAddress.Balance < feeInWei)
            {
                Log.Error("Insufficient funds for redeem for party");
                return;
            }

            using var addressLock = await EthereumAccount.AddressLocker
                .GetLockAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var (nonce, error) = await EthereumNonceManager.Instance
                .GetNonceAsync(EthConfig.GetEthereumApi(), walletAddress.Address, pending: true, cancellationToken)
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

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20Config.SwapContractAddress);

            var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

            var signResult = await EthereumAccount
                .SignAsync(txRequest, cancellationToken)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            var _ = await BroadcastTxAsync(
                    swap: swap,
                    txRequest: txRequest,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var erc20Config = Erc20Config;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.LastRefundTryTimeStamp + TimeSpan.FromMinutes(20) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync<EthereumTransaction>(
                    swap: swap,
                    localStorage: Erc20Account.LocalStorage,
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
                var (tx, error) = await Erc20SwapInitiatedHelper
                    .TryToFindPaymentAsync(swap, erc20Config, cancellationToken)
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
                Log.Error(e, $"Can't check {swap.Id} swap initiation for {EthConfig.Name}");
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var (gasPrice, gasPriceError) = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return;
            }

            var walletAddress = await EthereumAccount
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for refund from local db", swap.FromAddress);
                return;
            }

            var feeInWei = erc20Config.RefundGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

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

                var message = new Erc20RefundMessage
                {
                    FromAddress          = walletAddress.Address,
                    HashedSecret         = swap.SecretHash,
                    MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                    MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                    Nonce                = nonce,
                    TransactionType      = EthereumHelper.Eip1559TransactionType
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.RefundGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(erc20Config.SwapContractAddress);

                var txRequest = new EthereumTransactionRequest(txInput, EthConfig.ChainId);

                var signResult = await EthereumAccount
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
                localStorage: Erc20Account.LocalStorage,
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
            _ = Task.Run(() => Erc20SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                erc20Config: Erc20Config,
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
            _ = Task.Run(() => Erc20SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                erc20Config: Erc20Config,
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
            return await Erc20SwapInitiatedHelper
                .TryToFindPaymentAsync(
                    swap: swap,
                    erc20Config: Erc20Config,
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
                var (isRefunded, error) = await Erc20SwapRefundedHelper
                    .IsRefundedAsync(
                        swap: swap,
                        erc20Config: Erc20Config,
                        attempts: EthereumSwap.MaxRefundCheckAttempts,
                        attemptIntervalInSec: EthereumSwap.RefundCheckAttemptIntervalInSec,
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
                        await Erc20Account
                            .UpdateBalanceAsync(swap.ToAddress, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Debug($"Erc20 swap update balance for address {swap.ToAddress} canceled");
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

        public static decimal GetRequiredAmount(Swap swap, Erc20Config erc20)
        {
            var requiredAmount = AmountHelper.QtyToSellAmount(swap.Side, swap.Qty, swap.Price, erc20.Precision);

            // maker network fee
            if (swap.MakerNetworkFee > 0 && swap.MakerNetworkFee < requiredAmount) // network fee size check
                requiredAmount += AmountHelper.RoundDown(swap.MakerNetworkFee, erc20.Precision);

            return requiredAmount;
        }

        protected async Task<EthereumTransactionRequest> CreatePaymentTxAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var erc20Config = Erc20Config;

            Log.Debug("Create payment transaction from address {@adderss} for swap {@swapId}", swap.FromAddress, swap.Id);

            var requiredAmount = GetRequiredAmount(swap, erc20Config);
            var requiredAmountInTokens = requiredAmount.ToTokens(erc20Config.Decimals);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            var rewardForRedeem = swap.PartyRewardForRedeem;

            var walletAddress = await Erc20Account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            var (gasPrice, gasPriceError) = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return null;
            }

            var balanceInWei = (await EthereumAccount
                .GetAddressBalanceAsync(
                    address: walletAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Confirmed;

            var feeAmountInWei = rewardForRedeem == 0
                ? erc20Config.InitiateGasLimit * gasPrice.MaxFeePerGas.GweiToWei()
                : erc20Config.InitiateWithRewardGasLimit * gasPrice.MaxFeePerGas.GweiToWei();

            if (balanceInWei < feeAmountInWei)
            {
                Log.Error(
                    "Insufficient funds at {@address} for fee. Balance: {@balance}, feeAmount: {@feeAmount}, result: {@result}",
                    walletAddress.Address,
                    balanceInWei,
                    feeAmountInWei,
                    balanceInWei - feeAmountInWei);

                return null;
            }

            var balanceInTokens = walletAddress.Balance;

            Log.Debug("Available balance: {@balance}", balanceInTokens);

            if (balanceInTokens < requiredAmountInTokens)
            {
                Log.Error(
                    "Insufficient funds at {@address}. Balance: {@balance}, required: {@result}, missing: {@missing}",
                    walletAddress.Address,
                    balanceInTokens,
                    requiredAmountInTokens,
                    balanceInTokens - requiredAmountInTokens);

                return null;
            }

            var amountInTokens = AmountHelper.DustProofMin(
                balanceInTokens,
                requiredAmountInTokens,
                erc20Config.DustDigitsMultiplier);

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

            var initMessage = new Erc20InitiateMessage
            {
                HashedSecret         = swap.SecretHash,
                ERC20Contract        = erc20Config.TokenContractAddress,
                Participant          = swap.PartyAddress,
                RefundTimestamp      = refundTimeStampUtcInSec,
                Countdown            = lockTimeInSeconds,
                Value                = amountInTokens,
                RedeemFee            = rewardForRedeem.ToTokens(erc20Config.Decimals),
                Active               = true,
                FromAddress          = walletAddress.Address,
                MaxFeePerGas         = gasPrice.MaxFeePerGas.GweiToWei(),
                MaxPriorityFeePerGas = gasPrice.MaxPriorityFeePerGas.GweiToWei(),
                Nonce                = nonce,
                TransactionType      = EthereumHelper.Eip1559TransactionType
            };

            var initiateGasLimit = rewardForRedeem == 0
                ? erc20Config.InitiateGasLimit
                : erc20Config.InitiateWithRewardGasLimit;

            initMessage.Gas = await EstimateGasAsync(initMessage, new BigInteger(initiateGasLimit))
                .ConfigureAwait(false);

            txInput = initMessage.CreateTransactionInput(erc20Config.SwapContractAddress);

            return new EthereumTransactionRequest(txInput, EthConfig.ChainId);
        }

        private async Task<IList<EthereumTransactionRequest>> CreateApproveTxsAsync(
            Swap swap,
            EthereumTransactionRequest paymentTx,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20Config;

            Log.Debug("Create approve transactions for swap {@swapId}", swap.Id);

            var transactions = new List<EthereumTransactionRequest>();

            var walletAddress = await Erc20Account
                .GetAddressAsync(paymentTx.From, cancellationToken)
                .ConfigureAwait(false);

            var (gasPrice, gasPriceError) = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (gasPriceError != null)
            {
                Log.Error("Get gas price error: {@code}. Message: {@message}", gasPriceError.Value.Code, gasPriceError.Value.Message);
                return null;
            }

            var allowanceMessage = new Erc20AllowanceMessage
            {
                Owner       = walletAddress.Address,
                Spender     = erc20.SwapContractAddress,
                FromAddress = walletAddress.Address
            };

            var (allowance, allowanceError) = await Erc20Config
                .GetErc20Api()
                .GetErc20AllowanceAsync(
                    tokenAddress: erc20.TokenContractAddress,
                    allowanceMessage: allowanceMessage,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (allowanceError != null)
            {
                Log.Error($"Getting allowance error: {allowanceError.Value.Message}");
                return new List<EthereumTransactionRequest>();
            }

            var (nonce, nonceError) = await EthConfig
                .GetEthereumApi()
                .GetTransactionsCountAsync(
                    address: walletAddress.Address,
                    pending: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (nonceError != null)
            {
                Log.Error($"Getting nonce error: {nonceError.Value.Message}");
                return new List<EthereumTransactionRequest>();
            }

            if (allowance > 0)
            {
                var tx = await CreateApproveTx(
                        walletAddress: walletAddress.Address,
                        nonce: nonce,
                        value: 0,
                        maxFeePerGas: gasPrice.MaxFeePerGas,
                        maxPriorityFeePerGas: gasPrice.MaxPriorityFeePerGas)
                    .ConfigureAwait(false);

                transactions.Add(tx);
            }

            var requiredAmount = GetRequiredAmount(swap, erc20);
            var requiredAmountInTokens = requiredAmount.ToTokens(erc20.Decimals);

            var amountInTokens = AmountHelper.DustProofMin(
                walletAddress.Balance,
                requiredAmountInTokens,
                erc20.DustDigitsMultiplier);

            var approveTx = await CreateApproveTx(
                    walletAddress: walletAddress.Address,
                    nonce: nonce,
                    value: amountInTokens,
                    maxFeePerGas: gasPrice.MaxFeePerGas,
                    maxPriorityFeePerGas: gasPrice.MaxPriorityFeePerGas)
                .ConfigureAwait(false);

            transactions.Add(approveTx);

            return transactions;
        }

        private async Task<EthereumTransactionRequest> CreateApproveTx(
            string walletAddress,
            BigInteger nonce,
            BigInteger value,
            decimal maxFeePerGas,
            decimal maxPriorityFeePerGas)
        {
            var erc20Config = Erc20Config;

            var message = new Erc20ApproveMessage
            {
                Spender              = erc20Config.SwapContractAddress,
                Value                = value,
                FromAddress          = walletAddress,
                MaxFeePerGas         = maxFeePerGas.GweiToWei(),
                MaxPriorityFeePerGas = maxPriorityFeePerGas.GweiToWei(),
                Nonce                = nonce,
                TransactionType      = EthereumHelper.Eip1559TransactionType
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.ApproveGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20Config.TokenContractAddress);

            return new EthereumTransactionRequest(txInput, chainId: EthConfig.ChainId);
        }

        private async Task<string> BroadcastTxAsync(
            Swap swap,
            EthereumTransactionRequest txRequest,
            CancellationToken cancellationToken = default)
        {
            var api = EthConfig.GetEtherScanApi();

            var (txId, error) = await api
                .BroadcastAsync(txRequest, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                throw new InternalException(error.Value);

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            var tx = new EthereumTransaction(txRequest, txId);

            await EthereumAccount
                .LocalStorage
                .UpsertTransactionAsync(
                    tx: tx,
                    notifyIfNewOrChanged: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
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
                    .EstimateGasAsync(Erc20Config.SwapContractAddress, message)
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
                await Task.Delay(EthereumSwap.InitiationCheckInterval, cancellationToken)
                    .ConfigureAwait(false);

                var tx = await EthereumAccount
                    .LocalStorage
                    .GetTransactionByIdAsync<EthereumTransaction>(EthereumHelper.Eth, txId)
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