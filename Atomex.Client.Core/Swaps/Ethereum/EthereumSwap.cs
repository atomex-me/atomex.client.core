using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Ethereum.Helpers;
using Atomex.Wallet.Abstract;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;
using Atomex.Swaps.Helpers;

namespace Atomex.Swaps.Ethereum
{
    public class EthereumSwap : CurrencySwap
    {
        public EthereumSwap(Currency currency, IAccount account, ISwapClient swapClient)
            : base(currency, account, swapClient)
        {
        }

        private Atomex.Ethereum Eth => (Atomex.Ethereum)Currency;

        public override async Task PayAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            var txs = (await CreatePaymentTxsAsync(swap, lockTimeInSeconds)
                .ConfigureAwait(false))
                .ToList();

            if (txs.Count == 0)
            {
                Log.Error("Can't create payment transactions");
                return;
            }

            var isInitiateTx = true;

            foreach (var tx in txs)
            {
                var signResult = await SignTransactionAsync(tx)
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
                    RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentSigned);
                }

                await BroadcastTxAsync(swap, tx)
                    .ConfigureAwait(false);

                if (isInitiateTx)
                {
                    swap.PaymentTx = tx;
                    swap.StateFlags |= SwapStateFlags.IsPaymentBroadcast;
                    RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

                    isInitiateTx = false;

                    // delay for contract initiation
                    if (txs.Count > 1)
                        await Task.Delay(TimeSpan.FromSeconds(60))
                            .ConfigureAwait(false);
                }
            }

            // start redeem control async
            EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Currency,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemedHandler,
                    canceledHandler: RedeemControlCanceledHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task PrepareToReceiveAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Action<ClientSwap, CancellationToken>(SwapAcceptedHandler)
                : new Action<ClientSwap, CancellationToken>(SwapInitiatedHandler);

            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();

            EthereumSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: Currency,
                    refundTimeStamp: refundTimeUtcInSec,
                    interval: DefaultConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create redeem for swap {@swapId}", swap.Id);

            var walletAddress = (await Account
                .GetUnspentAddressesAsync(
                    toAddress: swap.ToAddress,
                    currency: Currency,
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

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(Eth, walletAddress.Address, cancellationToken)
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
                GasPrice = Atomex.Ethereum.GweiToWei(Eth.GasPriceInGwei),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(Eth.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(Eth.SwapContractAddress);

            var redeemTx = new EthereumTransaction(Eth, txInput)
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
                    currency: Currency,
                    txId: redeemTx.Id,
                    confirmationHandler: RedeemConfirmedHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override async Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // get refund address
                    currency: Currency,
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

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(Eth, walletAddress.Address)
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
                GasPrice = Atomex.Ethereum.GweiToWei(Eth.GasPriceInGwei),
                Nonce = nonceResult.Value,
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(Eth.RefundGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(Eth.SwapContractAddress);

            var refundTx = new EthereumTransaction(Eth, txInput)
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
                    currency: Currency,
                    txId: refundTx.Id,
                    confirmationHandler: RefundConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override Task WaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Currency,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemedBySomeoneHandler,
                    canceledHandler: RedeemPartyControlCanceledHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task PartyRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create redeem for counterParty for swap {@swapId}", swap.Id);

            var walletAddress = (await Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get participant address
                    currency: Currency,
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

            var nonceResult = await EthereumNonceManager.Instance
                .GetNonceAsync(Eth, walletAddress.Address, cancellationToken)
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
                GasPrice = Atomex.Ethereum.GweiToWei(Eth.GasPriceInGwei),
            };

            message.Gas = await EstimateGasAsync(message, new BigInteger(Eth.RedeemGasLimit))
                .ConfigureAwait(false);

            var txInput = message.CreateTransactionInput(Eth.SwapContractAddress);

            var redeemTx = new EthereumTransaction(Eth, txInput)
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

        public override Task RestoreSwapForSoldCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemSigned))
                    return Task.CompletedTask; // we already have redeem, let's check it in RestoreForPurchasedCurrency

                if (!(swap.PaymentTx is EthereumTransaction))
                {
                    Log.Error("Can't restore swap {@id}. Payment tx is null.", swap.Id);
                    return Task.CompletedTask;
                }

                var lockTimeInSeconds = swap.IsInitiator
                    ? DefaultInitiatorLockTimeInSeconds
                    : DefaultAcceptorLockTimeInSeconds;

                // start redeem control async
                EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                        swap: swap,
                        currency: Currency,
                        refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                        interval: TimeSpan.FromSeconds(30),
                        cancelOnlyIfRefundTimeReached: true,
                        redeemedHandler: RedeemedHandler,
                        canceledHandler: RedeemControlCanceledHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
            else
            {
                if (DateTime.UtcNow < swap.TimeStamp.ToUniversalTime() + DefaultMaxSwapTimeout)
                {
                    if (swap.IsInitiator)
                    {
                        // todo: initiate swap

                        //await InitiateSwapAsync(swapState)
                        //    .ConfigureAwait(false);
                    }
                    else
                    {
                        // todo: request secret hash from server
                    }
                }
                else
                {
                    swap.Cancel();
                    RaiseSwapUpdated(swap, SwapStateFlags.IsCanceled);
                }
            }

            return Task.CompletedTask;
        }

        public override async Task RestoreSwapForPurchasedCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.RewardForRedeem > 0 &&
                swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                // may be swap already redeemed by someone else
                await WaitForRedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemSigned) &&
                     !swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                // redeem tx created, signed, but not broadcast.
                // there is a possibility that tx could successfully broadcast
                // otherwise try again

                EthereumSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                        swap: swap,
                        currency: Currency,
                        refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                        interval: TimeSpan.FromSeconds(30),
                        cancelOnlyIfRefundTimeReached: false,
                        redeemedHandler: RedeemedBySomeoneHandler,
                        canceledHandler: RedeemPartyControlCanceledHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
            else if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                    !swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemConfirmed))
            {
                if (!(swap.RedeemTx is EthereumTransaction redeemTx))
                {
                    Log.Error("Can't restore swap {@id}. Redeem tx is null", swap.Id);
                    return;
                }

                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: Currency,
                        txId: redeemTx.Id,
                        confirmationHandler: RedeemConfirmedHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
        }

        #region Event Handlers

        private void SwapInitiatedHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug(
                "Initiator payment transaction received. Now counter party can broadcast payment tx for swap {@swapId}", 
                swap.Id);

            swap.StateFlags |= SwapStateFlags.HasPartyPayment;
            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        private async void SwapAcceptedHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug(
                    "Acceptors payment transaction received. Now initiator can do self redeem and do party redeem for acceptor (if needs and wants) for swap {@swapId}.",
                    swap.Id);

                swap.StateFlags |= SwapStateFlags.HasPartyPayment;
                swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
                RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

                RaiseAcceptorPaymentConfirmed(swap);

                await RedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap accepted error");
            }
        }

        private void SwapCanceledHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // todo: do smth here
            Log.Debug("Swap canceled due to wrong counter party params {@swapId}", swap.Id);
        }

        private void RedeemConfirmedHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private void RedeemedHandler(
            ClientSwap swap,
            byte[] secret,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = secret;
                RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);

                RaiseAcceptorPaymentSpent(swap);
            }
        }

        private void RedeemControlCanceledHandler(
            ClientSwap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control canceled event for swap {@swapId}", swap.Id);

            ControlRefundTimeAsync(
                    swap: swap,
                    refundTimeUtc: refundTimeUtc,
                    refundTimeReachedHandler: RefundTimeReachedHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        private async void RefundTimeReachedHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await EthereumSwapRefundedHelper.IsRefundedAsync(
                        swap: swap,
                        currency: Currency,
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

        private void RefundConfirmedEventHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRefundConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemedBySomeoneHandler(
            ClientSwap swap,
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
                AddressHelper.UpdateAddressBalanceAsync(
                        account: Account,
                        currency: Currency,
                        address: swap.ToAddress,
                        cancellationToken: cancellationToken)
                    .FireAndForget();
            }
        }

        private async void RedeemPartyControlCanceledHandler(
            ClientSwap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken)
        {
            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    var walletAddress = (await Account
                        .GetUnspentAddressesAsync(
                            toAddress: swap.ToAddress,
                            currency: Currency,
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

        #endregion Event Handlers

        #region Helpers

        private async Task<IEnumerable<EthereumTransaction>> CreatePaymentTxsAsync(
            ClientSwap swap,
            int lockTimeInSeconds,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var requiredAmountInEth = AmountHelper.QtyToAmount(swap.Side, swap.Qty, swap.Price);
            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds)).ToUnixTimeSeconds();
            var isInitTx = true;
            var rewardForRedeemInEth = swap.PartyRewardForRedeem;

            var unspentAddresses = (await Account
                .GetUnspentAddressesAsync(Eth, cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()));

            var transactions = new List<EthereumTransaction>();

            foreach (var walletAddress in unspentAddresses)
            {
                Log.Debug("Create swap payment tx from address {@address} for swap {@swapId}", walletAddress.Address, swap.Id);

                var balanceInEth = (await Account
                    .GetAddressBalanceAsync(
                        currency: Eth,
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInEth);

                var feeAmountInEth = isInitTx
                    ? rewardForRedeemInEth == 0
                        ? Eth.InitiateFeeAmount
                        : Eth.InitiateWithRewardFeeAmount
                    : Eth.AddFeeAmount;

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
                    .GetNonceAsync(Eth, walletAddress.Address)
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
                        GasPrice = Atomex.Ethereum.GweiToWei(Eth.GasPriceInGwei),
                        Nonce = nonceResult.Value,
                        RedeemFee = Atomex.Ethereum.EthToWei(rewardForRedeemInEth)
                    };

                    var initiateGasLimit = rewardForRedeemInEth == 0
                        ? Eth.InitiateGasLimit
                        : Eth.InitiateWithRewardGasLimit;

                    message.Gas = await EstimateGasAsync(message, new BigInteger(initiateGasLimit))
                        .ConfigureAwait(false);

                    txInput = message.CreateTransactionInput(Eth.SwapContractAddress);
                }
                else
                {
                    var message = new AddFunctionMessage
                    {
                        HashedSecret = swap.SecretHash,
                        AmountToSend = Atomex.Ethereum.EthToWei(amountInEth),
                        FromAddress = walletAddress.Address,
                        GasPrice = Atomex.Ethereum.GweiToWei(Eth.GasPriceInGwei),
                        Nonce = nonceResult.Value,
                    };

                    message.Gas = await EstimateGasAsync(message, new BigInteger(Eth.AddGasLimit))
                        .ConfigureAwait(false);

                    txInput = message.CreateTransactionInput(Eth.SwapContractAddress);
                }

                transactions.Add(new EthereumTransaction(Eth, txInput)
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
            var walletAddress = await Account
                .ResolveAddressAsync(
                    currency: tx.Currency,
                    address: tx.From,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return await Account.Wallet
                .SignAsync(
                    tx: tx,
                    address: walletAddress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task BroadcastTxAsync(
            ClientSwap swap,
            EthereumTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var broadcastResult = await Eth.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new InternalException(broadcastResult.Error);

            var txId = broadcastResult.Value;

            if (txId == null)
                throw new Exception("Transaction Id is null");

            Log.Debug("TxId {@id} for swap {@swapId}", txId, swap.Id);

            // account new unconfirmed transaction
            await Account
                .UpsertTransactionAsync(
                    tx: tx,
                    updateBalance: true,
                    notifyIfUnconfirmed: true,
                    notifyIfBalanceUpdated: true,
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
                var web3 = new Web3(Web3BlockchainApi.UriByChain(Eth.Chain));
                var txHandler = web3.Eth.GetContractTransactionHandler<TMessage>();

                var estimatedGas = await txHandler
                    .EstimateGasAsync(Eth.SwapContractAddress, message)
                    .ConfigureAwait(false);

                Log.Debug("Estimated gas {@gas}", estimatedGas?.Value.ToString());

                return estimatedGas?.Value ?? defaultGas;
            }
            catch (Exception)
            {
                Log.Debug("Error while estimating fee");
            }

            return defaultGas;
        }

        #endregion Helpers
    }
}