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
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Ethereum.ERC20.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Ethereum;

namespace Atomex.Swaps.Ethereum
{
    public class Erc20Swap : CurrencySwap
    {
        private Erc20Account Erc20Account { get; }
        private EthereumAccount_OLD EthereumAccount { get; }
        private Erc20Config Erc20Config => Currencies.Get<Erc20Config>(Currency);
        private EthereumConfig_ETH EthConfig => Currencies.Get<EthereumConfig_ETH>(EthereumAccount.Currency);

        public Erc20Swap(
            Erc20Account account,
            EthereumAccount_OLD ethereumAccount,
            ICurrencies currencies)
            : base(account.Currency, currencies)
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

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var paymentTx = await CreatePaymentTxAsync(swap, lockTimeInSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (paymentTx == null)
            {
                Log.Error("Can't create payment transaction");
                return;
            }

            try
            {
                try
                {
                    await EthereumAccount_OLD.AddressLocker
                        .LockAsync(paymentTx.From, cancellationToken)
                        .ConfigureAwait(false);

                    var txsToBroadcast = await CreateApproveTxsAsync(swap, paymentTx, cancellationToken)
                        .ConfigureAwait(false) ?? throw new Exception($"Can't get allowance for {paymentTx.From}");

                    txsToBroadcast.Add(paymentTx);

                    foreach (var tx in txsToBroadcast)
                    {
                        var isInitiateTx = tx.Type.HasFlag(BlockchainTransactionType.SwapPayment);

                        var nonceResult = await EthereumNonceManager.Instance
                            .GetNonceAsync(Erc20Config, tx.From, pending: true, cancellationToken)
                            .ConfigureAwait(false);

                        if (nonceResult.HasError)
                        {
                            Log.Error("Nonce getting error with code {@code} and description {@description}",
                                nonceResult.Error.Code,
                                nonceResult.Error.Description);

                            return;
                        }

                        tx.Nonce = nonceResult.Value;

                        var signResult = await SignTransactionAsync(tx, cancellationToken)
                            .ConfigureAwait(false);

                        if (!signResult)
                        {
                            Log.Error("Transaction signing error");
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
                                updateBalance: isInitiateTx,
                                notifyIfUnconfirmed: true,
                                notifyIfBalanceUpdated: isInitiateTx,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch
                {
                    throw;
                }
                finally
                {
                    EthereumAccount_OLD.AddressLocker.Unlock(paymentTx.From);
                }

                swap.PaymentTx = paymentTx;
                swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;

                await UpdateSwapAsync(swap, SwapStateFlags.IsPaymentBroadcast, cancellationToken)
                    .ConfigureAwait(false);

                var isInitiateConfirmed = await WaitPaymentConfirmationAsync(
                            txId: paymentTx.Id,
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

            _ = ERC20SwapInitiatedHelper.StartSwapInitiatedControlAsync(
                swap: swap,
                currency: Erc20Config,
                lockTimeInSec: lockTimeInSeconds,
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
            var erc20Config = Erc20Config;

            var secretResult = await ERC20SwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: erc20Config,
                    attempts: EthereumSwap.MaxRedeemCheckAttempts,
                    attemptIntervalInSec: EthereumSwap.RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!secretResult.HasError && secretResult.Value != null)
            {
                await RedeemConfirmedEventHandler(swap, null, cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                // redeem already broadcast
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: erc20Config,
                    dataRepository: Erc20Account.DataRepository,
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

            var gasPrice = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = await EthereumAccount
                .GetAddressAsync(swap.RedeemFromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem from local db", swap.RedeemFromAddress);
                return;
            }

            var feeInEth = EthConfig.GetFeeAmount(erc20Config.RedeemGasLimit, gasPrice);

            if (walletAddress.Balance < feeInEth)
            {
                Log.Error("Insufficient funds for redeem");
                return;
            }

            EthereumTransaction_OLD redeemTx;

            try
            {
                await EthereumAccount_OLD.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(erc20Config, walletAddress.Address, pending: true, cancellationToken)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                {
                    Log.Error("Nonce getting error with code {@code} and description {@description}",
                        nonceResult.Error.Code,
                        nonceResult.Error.Description);

                    return;
                }

                var message = new ERC20RedeemFunctionMessage
                {
                    FromAddress  = walletAddress.Address,
                    HashedSecret = swap.SecretHash,
                    Secret       = swap.Secret,
                    Nonce        = nonceResult.Value,
                    GasPrice     = EthereumConfig_ETH.GweiToWei(gasPrice),
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.RedeemGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(erc20Config.SwapContractAddress);

                redeemTx = new EthereumTransaction_OLD(erc20Config.Name, txInput)
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
                EthereumAccount_OLD.AddressLocker.Unlock(walletAddress.Address);
            }

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRedeemBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: erc20Config,
                dataRepository: Erc20Account.DataRepository,
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

            var erc20Config = Erc20Config;

            var gasPrice = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = await EthereumAccount
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for redeem for party from local db", swap.FromAddress);
                return;
            }

            var feeInEth = EthConfig.GetFeeAmount(erc20Config.RedeemGasLimit, gasPrice);

            if (walletAddress.Balance < feeInEth)
            {
                Log.Error("Insufficient funds for redeem for party");
                return;
            }

            using var addressLock = await EthereumAccount_OLD.AddressLocker
                .GetLockAsync(walletAddress.Address, cancellationToken)
                .ConfigureAwait(false);

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(erc20Config, walletAddress.Address, pending: true, cancellationToken)
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

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20Config.SwapContractAddress);

            var redeemTx = new EthereumTransaction_OLD(erc20Config.Name, txInput)
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
            var erc20Config = Erc20Config;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(20) > DateTime.UtcNow)
            {
                _ = TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: erc20Config,
                    dataRepository: Erc20Account.DataRepository,
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
                var txResult = await ERC20SwapInitiatedHelper
                    .TryToFindPaymentAsync(swap, erc20Config, cancellationToken)
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
                Log.Error(e, $"Can't check {swap.Id} swap initiation for {EthConfig.Name}");
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var gasPrice = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = await EthereumAccount
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            if (walletAddress == null)
            {
                Log.Error("Can't get address {@address} for refund from local db", swap.FromAddress);
                return;
            }

            var feeInEth = EthConfig.GetFeeAmount(erc20Config.RefundGasLimit, gasPrice);

            if (walletAddress.Balance < feeInEth)
            {
                Log.Error("Insufficient funds for refund");
                return;
            }

            EthereumTransaction_OLD refundTx;

            try
            {
                await EthereumAccount_OLD.AddressLocker
                    .LockAsync(walletAddress.Address, cancellationToken)
                    .ConfigureAwait(false);

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(erc20Config, walletAddress.Address, pending: true, cancellationToken)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                {
                    Log.Error("Nonce getting error with code {@code} and description {@description}",
                        nonceResult.Error.Code,
                        nonceResult.Error.Description);

                    return;
                }

                var message = new ERC20RefundFunctionMessage
                {
                    FromAddress  = walletAddress.Address,
                    HashedSecret = swap.SecretHash,
                    GasPrice     = EthereumConfig_ETH.GweiToWei(gasPrice),
                    Nonce        = nonceResult.Value,
                };

                message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.RefundGasLimit))
                    .ConfigureAwait(false);

                var txInput = message.CreateTransactionInput(erc20Config.SwapContractAddress);

                refundTx = new EthereumTransaction_OLD(erc20Config.Name, txInput)
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
                EthereumAccount_OLD.AddressLocker.Unlock(walletAddress.Address);
            }

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;

            await UpdateSwapAsync(swap, SwapStateFlags.IsRefundBroadcast, cancellationToken)
                .ConfigureAwait(false);

            _ = TrackTransactionConfirmationAsync(
                swap: swap,
                currency: erc20Config,
                dataRepository: Erc20Account.DataRepository,
                txId: refundTx.Id,
                confirmationHandler: RefundConfirmedEventHandler,
                cancellationToken: cancellationToken);
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            _ = ERC20SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: Erc20Config,
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
            _ = ERC20SwapRedeemedHelper.StartSwapRedeemedControlAsync(
                swap: swap,
                currency: Erc20Config,
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

            return await ERC20SwapInitiatedHelper
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
                var isRefundedResult = await ERC20SwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: Erc20Config,
                        attempts: EthereumSwap.MaxRefundCheckAttempts,
                        attemptIntervalInSec: EthereumSwap.RefundCheckAttemptIntervalInSec,
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
                _ = AddressHelper.UpdateAddressBalanceAsync<Erc20WalletScanner, Erc20Account, EthereumAccount_OLD>(
                    account: Erc20Account,
                    baseAccount: EthereumAccount,
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

        private decimal RequiredAmountInTokens(Swap swap, Erc20Config erc20)
        {
            var requiredAmountInERC20 = AmountHelper.QtyToSellAmount(swap.Side, swap.Qty, swap.Price, erc20.DigitsMultiplier);

            // maker network fee
            if (swap.MakerNetworkFee > 0 && swap.MakerNetworkFee < requiredAmountInERC20) // network fee size check
                requiredAmountInERC20 += AmountHelper.RoundDown(swap.MakerNetworkFee, erc20.DigitsMultiplier);

            return requiredAmountInERC20;
        }

        protected async Task<EthereumTransaction_OLD> CreatePaymentTxAsync(
            Swap swap,
            int lockTimeInSeconds,
            CancellationToken cancellationToken = default)
        {
            var erc20Config = Erc20Config;

            Log.Debug("Create payment transaction from address {@adderss} for swap {@swapId}", swap.FromAddress, swap.Id);

            var requiredAmountInERC20 = RequiredAmountInTokens(swap, erc20Config);

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            var rewardForRedeemInERC20 = swap.PartyRewardForRedeem;

            var walletAddress = await Erc20Account
                .GetAddressAsync(swap.FromAddress, cancellationToken)
                .ConfigureAwait(false);

            var gasPrice = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var balanceInEth = (await EthereumAccount
                .GetAddressBalanceAsync(
                    address: walletAddress.Address,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .Available;

            var feeAmountInEth = rewardForRedeemInERC20 == 0
                ? erc20Config.InitiateFeeAmount(gasPrice)
                : erc20Config.InitiateWithRewardFeeAmount(gasPrice);

            if (balanceInEth < feeAmountInEth)
            {
                Log.Error(
                    "Insufficient funds at {@address} for fee. Balance: {@balance}, feeAmount: {@feeAmount}, result: {@result}.",
                    walletAddress.Address,
                    balanceInEth,
                    feeAmountInEth,
                    balanceInEth - feeAmountInEth);

                return null;
            }

            var balanceInErc20 = walletAddress.Balance;

            Log.Debug("Available balance: {@balance}", balanceInErc20);

            if (balanceInErc20 < requiredAmountInERC20)
            {
                Log.Error(
                    "Insufficient funds at {@address}. Balance: {@balance}, required: {@result}, missing: {@missing}.",
                    walletAddress.Address,
                    balanceInErc20,
                    requiredAmountInERC20,
                    balanceInErc20 - requiredAmountInERC20);

                return null;
            }

            var amountInErc20 = AmountHelper.DustProofMin(
                balanceInErc20,
                requiredAmountInERC20,
                erc20Config.DigitsMultiplier,
                erc20Config.DustDigitsMultiplier);

            var nonceResult = await ((IEthereumBlockchainApi_OLD)erc20Config.BlockchainApi)
                .GetTransactionCountAsync(walletAddress.Address, pending: false, cancellationToken)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error($"Getting nonce error: {nonceResult.Error.Description}");
                return null;
            }

            TransactionInput txInput;

            var initMessage = new ERC20InitiateFunctionMessage
            {
                HashedSecret    = swap.SecretHash,
                ERC20Contract   = erc20Config.ERC20ContractAddress,
                Participant     = swap.PartyAddress,
                RefundTimestamp = refundTimeStampUtcInSec,
                Countdown       = lockTimeInSeconds,
                Value           = erc20Config.TokensToTokenDigits(amountInErc20),
                RedeemFee       = erc20Config.TokensToTokenDigits(rewardForRedeemInERC20),
                Active          = true,
                FromAddress     = walletAddress.Address,
                GasPrice        = EthereumConfig_ETH.GweiToWei(gasPrice),
                Nonce           = nonceResult.Value
            };

            var initiateGasLimit = rewardForRedeemInERC20 == 0
                ? erc20Config.InitiateGasLimit
                : erc20Config.InitiateWithRewardGasLimit;

            initMessage.Gas = await EstimateGasAsync(initMessage, new BigInteger(initiateGasLimit))
                .ConfigureAwait(false);

            txInput = initMessage.CreateTransactionInput(erc20Config.SwapContractAddress);

            return new EthereumTransaction_OLD(erc20Config.Name, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
            };
        }

        private async Task<IList<EthereumTransaction_OLD>> CreateApproveTxsAsync(
            Swap swap,
            EthereumTransaction_OLD paymentTx,
            CancellationToken cancellationToken = default)
        {
            var erc20 = Erc20Config;

            Log.Debug("Create approve transactions for swap {@swapId}", swap.Id);

            var transactions = new List<EthereumTransaction_OLD>();

            var walletAddress = await Erc20Account
                .GetAddressAsync(paymentTx.From, cancellationToken)
                .ConfigureAwait(false);

            var gasPrice = await EthConfig
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var allowanceMessage = new ERC20AllowanceFunctionMessage
            {
                Owner       = walletAddress.Address,
                Spender     = erc20.SwapContractAddress,
                FromAddress = walletAddress.Address
            };

            var allowance = await ((IEthereumBlockchainApi_OLD)erc20.BlockchainApi)
                .GetERC20AllowanceAsync(
                    erc20: erc20,
                    tokenAddress: erc20.ERC20ContractAddress,
                    allowanceMessage: allowanceMessage,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var nonceResult = await ((IEthereumBlockchainApi_OLD)erc20.BlockchainApi)
                .GetTransactionCountAsync(walletAddress.Address, pending: false, cancellationToken)
                .ConfigureAwait(false);

            if (nonceResult.HasError)
            {
                Log.Error($"Getting nonce error: {nonceResult.Error.Description}");
                return new List<EthereumTransaction_OLD>();
            }

            if (allowance.Value > 0)
            {
                var tx = await CreateApproveTx(
                        walletAddress: walletAddress.Address,
                        nonce: nonceResult.Value,
                        value: 0,
                        gasPrice: gasPrice)
                    .ConfigureAwait(false);

                transactions.Add(tx);
            }

            var requiredAmountInErc20 = RequiredAmountInTokens(swap, erc20);

            var amountInErc20 = AmountHelper.DustProofMin(
                walletAddress.Balance,
                requiredAmountInErc20,
                erc20.DigitsMultiplier,
                erc20.DustDigitsMultiplier);

            var approveTx = await CreateApproveTx(
                    walletAddress: walletAddress.Address,
                    nonce: nonceResult.Value,
                    value: erc20.TokensToTokenDigits(amountInErc20),
                    gasPrice: gasPrice)
                .ConfigureAwait(false);

            transactions.Add(approveTx);

            return transactions;
        }

        private async Task<EthereumTransaction_OLD> CreateApproveTx(
            string walletAddress,
            BigInteger nonce,
            BigInteger value,
            decimal gasPrice)
        {
            var erc20Config = Erc20Config;

            var message = new ERC20ApproveFunctionMessage
            {
                Spender     = erc20Config.SwapContractAddress,
                Value       = value,
                FromAddress = walletAddress,
                GasPrice    = EthereumConfig_ETH.GweiToWei(gasPrice),
                Nonce       = nonce,
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(erc20Config.ApproveGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(erc20Config.ERC20ContractAddress);

            return new EthereumTransaction_OLD(erc20Config.Name, txInput)
            {
                Type = BlockchainTransactionType.Output | BlockchainTransactionType.TokenApprove
            };
        }

        private async Task<bool> SignTransactionAsync(
            EthereumTransaction_OLD tx,
            CancellationToken cancellationToken = default)
        {
            var walletAddress = await Erc20Account
                .GetAddressAsync(
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await Erc20Account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    currency: Erc20Config,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            EthereumTransaction_OLD tx,
            CancellationToken cancellationToken = default,
            bool updateBalance = true,
            bool notifyIfUnconfirmed = true,
            bool notifyIfBalanceUpdated = true)
        {
            var broadcastResult = await Erc20Config.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new InternalException(broadcastResult.Error);

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            if (tx.Type.HasFlag(BlockchainTransactionType.SwapPayment))
                tx = tx.ParseERC20Input();

            // account new unconfirmed transaction
            await Erc20Account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var ethTx = tx.Clone();
            ethTx.Currency = EthConfig.Name;
            ethTx.Amount = 0;
            ethTx.Type = BlockchainTransactionType.Output | (ethTx.Type.HasFlag(BlockchainTransactionType.TokenApprove)
                ? BlockchainTransactionType.TokenCall
                : BlockchainTransactionType.SwapCall);

            await EthereumAccount
                .UpsertTransactionAsync(
                    tx: ethTx,
                    updateBalance: updateBalance,
                    notifyIfUnconfirmed: notifyIfUnconfirmed,
                    notifyIfBalanceUpdated: notifyIfBalanceUpdated,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // todo: transaction receipt status control
        }

        private async Task<BigInteger> EstimateGasAsync<TMessage>(
            TMessage message,
            BigInteger defaultGas) where TMessage : FunctionMessage, new()
        {
            try
            {
                var web3 = new Web3(EthereumSwap.UriByChain(EthConfig.Chain));
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

                var tx = await Erc20Account
                    .DataRepository
                    .GetTransactionByIdAsync(Erc20Config.Name, txId, Erc20Config.TransactionType)
                    .ConfigureAwait(false);

                if (tx is not { IsConfirmed: true }) continue;

                return tx.IsConfirmed;
            }

            return false;
        }

        #endregion Helpers
    }
}