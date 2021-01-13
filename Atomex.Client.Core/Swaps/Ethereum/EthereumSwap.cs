using System;
using System.Collections.Generic;
using System.Linq;
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
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Ethereum.Helpers;
using Atomex.Swaps.Helpers;
using Atomex.Wallet.Ethereum;
using Nethereum.Signer;

namespace Atomex.Swaps.Ethereum
{
    public class EthereumSwap : CurrencySwap
    {
        protected const int MaxRedeemCheckAttempts = 2;
        protected const int MaxRefundCheckAttempts = 2;
        protected const int RedeemCheckAttemptIntervalInSec = 5;
        protected const int RefundCheckAttemptIntervalInSec = 5;
        protected static TimeSpan InitiationTimeout = TimeSpan.FromMinutes(10);
        protected static TimeSpan InitiationCheckInterval = TimeSpan.FromSeconds(15);
        private Atomex.Ethereum Eth => Currencies.Get<Atomex.Ethereum>(Currency);
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
            if (!CheckPayRelevance(swap))
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

            var isInitiateTx = true;

            try
            {

                foreach (var paymentTx in paymentTxs)
                {
                    var signResult = await SignTransactionAsync(paymentTx, cancellationToken)
                        .ConfigureAwait(false);

                    if (!signResult)
                    {
                        Log.Error("Transaction signing error");
                        return;
                    }

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = paymentTx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentSigned;
                        RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentSigned);
                    }

                    await BroadcastTxAsync(swap, paymentTx, cancellationToken)
                        .ConfigureAwait(false);

                    if (isInitiateTx)
                    {
                        swap.PaymentTx = paymentTx;
                        swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
                        RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

                        isInitiateTx = false;

                        // check initiate payment tx confirmation
                        if (paymentTxs.Count > 1)
                        {
                            var isInitiated = await WaitPaymentConfirmationAsync(paymentTx.Id, InitiationTimeout, cancellationToken)
                                .ConfigureAwait(false);

                            if (!isInitiated)
                            {
                                Log.Error("Initiation payment tx not confirmed after timeout {@timeout}", InitiationTimeout.Minutes);
                                return;
                            }
                        }
                    }
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
                ? new Action<Swap, CancellationToken>(SwapAcceptedHandler)
                : new Action<Swap, CancellationToken>(SwapInitiatedHandler);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            EthereumSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: Eth,
                    refundTimeStamp: refundTimeUtcInSec,
                    interval: ConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

            var secretResult = await EthereumSwapRedeemedHelper
                .IsRedeemedAsync(
                    swap: swap,
                    currency: eth,
                    attempts: MaxRedeemCheckAttempts,
                    attemptIntervalInSec: RedeemCheckAttemptIntervalInSec,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!secretResult.HasError && secretResult.Value != null)
            {
                RedeemConfirmedEventHandler(swap, null, cancellationToken);
                return;
            }

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                swap.RedeemTx != null &&
                swap.RedeemTx.CreationTime != null &&
                swap.RedeemTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(30) > DateTime.UtcNow)
            {
                // redeem already broadcast
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: eth,
                        txId: swap.RedeemTx.Id,
                        confirmationHandler: RedeemConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

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

            var gasPrice = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = (await _account
                .GetUnspentAddressesAsync(
                    toAddress: swap.ToAddress,
                    amount: 0,
                    fee: 0,
                    feePrice: gasPrice,
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

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(eth, walletAddress.Address, cancellationToken)
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
                FromAddress = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                Secret = swap.Secret,
                Nonce = nonceResult.Value,
                GasPrice = Atomex.Ethereum.GweiToWei(gasPrice),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(Eth.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(eth.SwapContractAddress);

            var redeemTx = new EthereumTransaction(eth, txInput)
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
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemSigned);

            await BroadcastTxAsync(swap, redeemTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RedeemTx = redeemTx;
            swap.StateFlags |= SwapStateFlags.IsRedeemBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemBroadcast);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: eth,
                    txId: redeemTx.Id,
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override async Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

            if (swap.IsInitiator)
            {
                var partyRedeemDeadline = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds) - PartyRedeemTimeReserve;

                if (DateTime.UtcNow > partyRedeemDeadline)
                {
                    Log.Error("Party redeem dedline reached for swap {@swap}", swap.Id);
                    return;
                }
            }

            Log.Debug("Create redeem for counterParty for swap {@swapId}", swap.Id);

            var gasPrice = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = (await _account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get participant address
                    amount: 0,
                    fee: 0,
                    feePrice: gasPrice,
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

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(eth, walletAddress.Address, cancellationToken)
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
                FromAddress = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                Secret = swap.Secret,
                Nonce = nonceResult.Value,
                GasPrice = Atomex.Ethereum.GweiToWei(gasPrice),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(eth.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(eth.SwapContractAddress);

            var redeemTx = new EthereumTransaction(eth, txInput)
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
            var eth = Eth;

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast) &&
                swap.RefundTx != null &&
                swap.RefundTx.CreationTime != null &&
                swap.RefundTx.CreationTime.Value.ToUniversalTime() + TimeSpan.FromMinutes(20) > DateTime.UtcNow)
            {
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: eth,
                        txId: swap.RefundTx.Id,
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var gasPrice = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var walletAddress = (await _account
                .GetUnspentAddressesAsync(
                    toAddress: null, // get refund address
                    amount: 0,
                    fee: 0,
                    feePrice: gasPrice,
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

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(eth, walletAddress.Address)
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
                FromAddress = walletAddress.Address,
                HashedSecret = swap.SecretHash,
                GasPrice = Atomex.Ethereum.GweiToWei(gasPrice),
                Nonce = nonceResult.Value,
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(eth.RefundGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(eth.SwapContractAddress);

            var refundTx = new EthereumTransaction(eth, txInput)
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
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundSigned);

            await BroadcastTxAsync(swap, refundTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RefundTx = refundTx;
            swap.StateFlags |= SwapStateFlags.IsRefundBroadcast;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

            TrackTransactionConfirmationAsync(
                    swap: swap,
                    currency: eth,
                    txId: refundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Eth,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemCompletedEventHandler,
                    canceledHandler: RedeemCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Eth,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                    canceledHandler: RedeemBySomeoneCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
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

        protected override async void RefundTimeReachedHandler(
            Swap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await EthereumSwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: Eth,
                        attempts: MaxRefundCheckAttempts,
                        attemptIntervalInSec: RefundCheckAttemptIntervalInSec,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!isRefundedResult.HasError)
                {
                    if (isRefundedResult.Value)
                    {
                        RefundConfirmedEventHandler(swap, swap.RefundTx, cancellationToken);
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

        private void RedeemBySomeoneCompletedEventHandler(
            Swap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);

                // get transactions & update balance for address async
                AddressHelper.UpdateAddressBalanceAsync<EthereumWalletScanner, EthereumAccount>(
                        account: _account,
                        address: swap.ToAddress,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
        }

        private async void RedeemBySomeoneCanceledEventHandler(
            Swap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken)
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
                            feePrice: await Eth
                                .GetGasPriceAsync(cancellationToken)
                                .ConfigureAwait(false),
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

        #endregion Event Handlers

        #region Helpers

        protected virtual async Task<IEnumerable<EthereumTransaction>> CreatePaymentTxsAsync(
            Swap swap,
            int lockTimeInSeconds,
            CancellationToken cancellationToken = default)
        {
            var eth = Eth;

            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var requiredAmountInEth = AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price, eth.DigitsMultiplier);

            // maker miner fee
            if (swap.MakerMinerFee > 0 && swap.MakerMinerFee < requiredAmountInEth) // miner fee size check
                requiredAmountInEth += AmountHelper.RoundDown(swap.MakerMinerFee, eth.DigitsMultiplier);

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();
            var isInitTx = true;
            var rewardForRedeemInEth = swap.PartyRewardForRedeem;

            var unspentAddresses = (await _account
                .GetUnspentAddressesAsync(cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList(new AvailableBalanceAscending());

            var gasPrice = await eth
                .GetGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            var transactions = new List<EthereumTransaction>();

            foreach (var walletAddress in unspentAddresses)
            {
                Log.Debug("Create swap payment tx from address {@address} for swap {@swapId}", walletAddress.Address, swap.Id);

                var balanceInEth = (await _account
                    .GetAddressBalanceAsync(
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInEth);

                var feeAmountInEth = isInitTx
                    ? rewardForRedeemInEth == 0
                        ? eth.InitiateFeeAmount(gasPrice)
                        : eth.InitiateWithRewardFeeAmount(gasPrice)
                    : eth.AddFeeAmount(gasPrice);

                var amountInEth = Math.Min(balanceInEth - feeAmountInEth, requiredAmountInEth);

                if (amountInEth <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, feeAmount: {@feeAmount}, result: {@result}.",
                        walletAddress.Address,
                        balanceInEth,
                        feeAmountInEth,
                        amountInEth);

                    continue;
                }

                requiredAmountInEth -= amountInEth;

                var nonceResult = await EthereumNonceManager.Instance
                    .GetNonceAsync(eth, walletAddress.Address)
                    .ConfigureAwait(false);

                if (nonceResult.HasError)
                {
                    Log.Error("Nonce getting error with code {@code} and description {@description}",
                        nonceResult.Error.Code,
                        nonceResult.Error.Description);

                    return null;
                }

                TransactionInput txInput;

                if (isInitTx)
                {
                    var message = new InitiateFunctionMessage
                    {
                        HashedSecret = swap.SecretHash,
                        Participant = swap.PartyAddress,
                        RefundTimestamp = refundTimeStampUtcInSec,
                        AmountToSend = Atomex.Ethereum.EthToWei(amountInEth),
                        FromAddress = walletAddress.Address,
                        GasPrice = Atomex.Ethereum.GweiToWei(gasPrice),
                        Nonce = nonceResult.Value,
                        RedeemFee = Atomex.Ethereum.EthToWei(rewardForRedeemInEth)
                    };

                    var initiateGasLimit = rewardForRedeemInEth == 0
                        ? eth.InitiateGasLimit
                        : eth.InitiateWithRewardGasLimit;

                    message.Gas = await EstimateGasAsync(message, new BigInteger(initiateGasLimit))
                        .ConfigureAwait(false);

                    txInput = message.CreateTransactionInput(eth.SwapContractAddress);
                }
                else
                {
                    var message = new AddFunctionMessage
                    {
                        HashedSecret = swap.SecretHash,
                        AmountToSend = Atomex.Ethereum.EthToWei(amountInEth),
                        FromAddress = walletAddress.Address,
                        GasPrice = Atomex.Ethereum.GweiToWei(gasPrice),
                        Nonce = nonceResult.Value,
                    };

                    message.Gas = await EstimateGasAsync(message, new BigInteger(eth.AddGasLimit))
                        .ConfigureAwait(false);

                    txInput = message.CreateTransactionInput(eth.SwapContractAddress);
                }

                transactions.Add(new EthereumTransaction(eth, txInput)
                {
                    Type = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                });

                if (isInitTx)
                    isInitTx = false;

                if (requiredAmountInEth == 0)
                    break;
            }

            if (requiredAmountInEth > 0)
            {
                Log.Warning("Insufficient funds (left {@requiredAmount}).", requiredAmountInEth);
                return Enumerable.Empty<EthereumTransaction>();
            }

            return transactions;
        }

        private async Task<bool> SignTransactionAsync(
            EthereumTransaction tx,
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
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            Swap swap,
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var broadcastResult = await Eth.BlockchainApi
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
                var web3 = new Web3(UriByChain(Eth.Chain));
                var txHandler = web3.Eth.GetContractTransactionHandler<TMessage>();

                var estimatedGas = await txHandler
                    .EstimateGasAsync(Eth.SwapContractAddress, message)
                    .ConfigureAwait(false);

                Log.Debug("Estimated gas {@gas}", estimatedGas?.Value.ToString());

                var estimatedValue = estimatedGas?.Value ?? defaultGas;

                return defaultGas / estimatedValue > 2
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
        protected static string UriByChain(Chain chain)
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

                var tx = await Eth.BlockchainApi
                    .TryGetTransactionAsync(txId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (tx != null && !tx.HasError && tx.Value != null && tx.Value.State == BlockchainTransactionState.Confirmed)
                    return true;
            }

            return false;
        }

        #endregion Helpers
    }
}