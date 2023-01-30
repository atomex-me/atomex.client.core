using System;
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
using Atomex.Blockchain.Ethereum.Abstract;
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
        private IEthereumApi GetEthereumApi() => (IEthereumApi)EthConfig.BlockchainApi;

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
                        .GetNonceAsync(GetEthereumApi(), txRequest.From)
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
                            type: TransactionType.Output | TransactionType.SwapPayment | TransactionType.ContractCall,
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

            string txId = null;

            try
            {
                await EthereumAccount.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var (nonce, nonceError) = await EthereumNonceManager.Instance
                    .GetNonceAsync(
                        api: GetEthereumApi(),
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
                    FromAddress  = walletAddress.Address,
                    HashedSecret = swap.SecretHash,
                    Secret       = swap.Secret,
                    Nonce        = nonce,
                    GasPrice     = EthereumConfig.GweiToWei(gasPrice),
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
                        type: TransactionType.Output | TransactionType.SwapRedeem | TransactionType.ContractCall,
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

            var (nonce, error) = await EthereumNonceManager.Instance
                .GetNonceAsync(
                    api: GetEthereumApi(),
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
                FromAddress  = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                Secret       = swap.Secret,
                Nonce        = nonce,
                GasPrice     = EthereumConfig.GweiToWei(gasPrice),
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
                    type: TransactionType.Output | TransactionType.SwapRedeem | TransactionType.ContractCall,
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

            string txId = null;

            try
            {
                await EthereumAccount.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var (nonce, error) = await EthereumNonceManager.Instance
                    .GetNonceAsync(
                        api: GetEthereumApi(),
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
                    FromAddress  = walletAddress.Address,
                    HashedSecret = swap.SecretHash,
                    GasPrice     = EthereumConfig.GweiToWei(gasPrice),
                    Nonce        = nonce,
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
                        type: TransactionType.Output | TransactionType.SwapRefund | TransactionType.ContractCall,
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

        protected virtual async Task<EthereumTransactionRequest> CreatePaymentTxAsync(
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

            var (nonce, error) = await GetEthereumApi()
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
                HashedSecret    = swap.SecretHash,
                Participant     = swap.PartyAddress,
                RefundTimestamp = refundTimeStampUtcInSec,
                AmountToSend    = EthereumConfig.EthToWei(requiredAmountInEth),
                FromAddress     = walletAddress.Address,
                GasPrice        = EthereumConfig.GweiToWei(gasPrice),
                Nonce           = nonce,
                RedeemFee       = EthereumConfig.EthToWei(rewardForRedeemInEth)
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
            TransactionType type,
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

            var tx = new EthereumTransaction(txRequest, type);

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