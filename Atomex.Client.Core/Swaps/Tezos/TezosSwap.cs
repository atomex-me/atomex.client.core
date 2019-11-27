using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Helpers;
using Atomex.Swaps.Tezos.Helpers;
using Atomex.Wallet.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Swaps.Tezos
{
    public class TezosSwap : CurrencySwap
    {
        private static TimeSpan InitiationTimeout = TimeSpan.FromMinutes(10);
        private static TimeSpan InitiationCheckInterval = TimeSpan.FromSeconds(30);
        private Atomex.Tezos Xtz => (Atomex.Tezos)Currency;

        public TezosSwap(Currency currency, IAccount account, ISwapClient swapClient)
            : base(currency, account, swapClient)
        {
        }

        public override async Task PayAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
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

            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                // start redeem control async
                await StartWaitForRedeemAsync(swap, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public override Task StartPartyPaymentControlAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // initiator waits "accepted" event, acceptor waits "initiated" event
            var initiatedHandler = swap.IsInitiator
                ? new Action<ClientSwap, CancellationToken>(SwapAcceptedEventHandler)
                : new Action<ClientSwap, CancellationToken>(SwapInitiatedEventHandler);

            var lockTimeSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            TezosSwapInitiatedHelper.StartSwapInitiatedControlAsync(
                    swap: swap,
                    currency: Currency,
                    refundTimeStamp: refundTimeUtcInSec,
                    interval: DefaultConfirmationCheckInterval,
                    initiatedHandler: initiatedHandler,
                    canceledHandler: SwapCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        public override async Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                // redeem already broadcast
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: Currency,
                        txId: swap.RedeemTx.Id,
                        confirmationHandler: RedeemConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

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

            var redeemTx = new TezosTransaction
            {
                Currency = Xtz,
                CreationTime = DateTime.UtcNow,
                From = walletAddress.Address,
                To = Xtz.SwapContractAddress,
                Amount = 0,
                Fee = Xtz.RedeemFee,
                GasLimit = Xtz.RedeemGasLimit,
                StorageLimit = Xtz.RedeemStorageLimit,
                Params = RedeemParams(swap),
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
                    confirmationHandler: RedeemConfirmedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        public override async Task RedeemForPartyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create redeem for acceptor for swap {@swapId}", swap.Id);

            var walletAddress = (await Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get party address
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

            var redeemTx = new TezosTransaction
            {
                Currency = Xtz,
                CreationTime = DateTime.UtcNow,
                From = walletAddress.Address,
                To = Xtz.SwapContractAddress,
                Amount = 0,
                Fee = Xtz.RedeemFee,
                GasLimit = Xtz.RedeemGasLimit,
                StorageLimit = Xtz.RedeemStorageLimit,
                Params = RedeemParams(swap),
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
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
            {
                TrackTransactionConfirmationAsync(
                        swap: swap,
                        currency: Currency,
                        txId: swap.RefundTx.Id,
                        confirmationHandler: RefundConfirmedEventHandler,
                        cancellationToken: cancellationToken)
                    .FireAndForget();

                return;
            }

            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await Account
                .GetUnspentAddressesAsync(
                    toAddress: null, // todo: get refund address
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

            var refundTx = new TezosTransaction
            {
                Currency = Xtz,
                CreationTime = DateTime.UtcNow,
                From = walletAddress.Address,
                To = Xtz.SwapContractAddress,
                Fee = Xtz.RefundFee,
                GasLimit = Xtz.RefundGasLimit,
                StorageLimit = Xtz.RefundStorageLimit,
                Params = RefundParams(swap),
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

        public override Task StartWaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            var lockTimeInSeconds = swap.IsInitiator
                ? DefaultInitiatorLockTimeInSeconds
                : DefaultAcceptorLockTimeInSeconds;

            // start redeem control async
            TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Currency,
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
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control async
            TezosSwapRedeemedHelper.StartSwapRedeemedControlAsync(
                    swap: swap,
                    currency: Currency,
                    refundTimeUtc: swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    interval: TimeSpan.FromSeconds(30),
                    cancelOnlyIfRefundTimeReached: true,
                    redeemedHandler: RedeemBySomeoneCompletedEventHandler,
                    canceledHandler: RedeemBySomeoneCanceledEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();

            return Task.CompletedTask;
        }

        #region Event Handlers

        private void SwapInitiatedEventHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug(
                "Initiator's payment transaction received. Now acceptor can broadcast payment tx for swap {@swapId}",
                swap.Id);

            swap.StateFlags |= SwapStateFlags.HasPartyPayment;
            swap.StateFlags |= SwapStateFlags.IsPartyPaymentConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        private async void SwapAcceptedEventHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug(
                    "Acceptor's payment transaction received. Now initiator can do self redeem and do party redeem for acceptor (if needs and wants) for swap {@swapId}.",
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

        private void SwapCanceledEventHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            // todo: do smth here
            Log.Debug("Swap canceled due to wrong counterParty params {@swapId}", swap.Id);
        }

        private void RedeemConfirmedEventHandler(
            ClientSwap swap,
            IBlockchainTransaction tx,
            CancellationToken cancellationToken = default)
        {
            swap.StateFlags |= SwapStateFlags.IsRedeemConfirmed;
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private void RedeemCompletedEventHandler(
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

        private void RedeemCanceledEventHandler(
            ClientSwap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Handle redeem control canceled event for swap {@swapId}", swap.Id);

            ControlRefundTimeAsync(
                    swap: swap,
                    refundTimeUtc: refundTimeUtc,
                    refundTimeReachedHandler: RefundTimeReachedEventHandler,
                    cancellationToken: cancellationToken)
                .FireAndForget();
        }

        private async void RefundTimeReachedEventHandler(
            ClientSwap swap,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            try
            {
                var isRefundedResult = await TezosSwapRefundedHelper.IsRefundedAsync(
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

        private void RedeemBySomeoneCompletedEventHandler(
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

        private async void RedeemBySomeoneCanceledEventHandler(
            ClientSwap swap,
            DateTime refundTimeUtc,
            CancellationToken cancellationToken = default)
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

        private async Task<IEnumerable<TezosTransaction>> CreatePaymentTxsAsync(
            ClientSwap swap,
            int lockTimeSeconds,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("Create payment transactions for swap {@swapId}", swap.Id);

            var requiredAmountInMtz = AmountHelper
                .QtyToAmount(swap.Side, swap.Qty, swap.Price)
                .ToMicroTez();

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();
            var isInitTx = true;
            var rewardForRedeemInMtz = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToMicroTez()
                : 0;

            var unspentAddresses = (await Account
                .GetUnspentAddressesAsync(Xtz, cancellationToken)
                .ConfigureAwait(false))
                .ToList()
                .SortList(new AvailableBalanceAscending(Account.AssetWarrantyManager));

            var transactions = new List<TezosTransaction>();

            foreach (var walletAddress in unspentAddresses)
            {
                Log.Debug("Create swap payment tx from address {@address} for swap {@swapId}",
                    walletAddress.Address,
                    swap.Id);

                var balanceInTz = (await Account
                    .GetAddressBalanceAsync(
                        currency: Xtz,
                        address: walletAddress.Address,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                    .Available;

                Log.Debug("Available balance: {@balance}", balanceInTz);

                var balanceInMtz = balanceInTz.ToMicroTez();

                var feeAmountInMtz = isInitTx
                    ? Xtz.InitiateFee
                    : Xtz.AddFee;

                var storageLimitInMtz = isInitTx
                    ? Xtz.InitiateStorageLimit * 1000
                    : Xtz.AddStorageLimit * 1000;

                var amountInMtz = Math.Min(balanceInMtz - feeAmountInMtz - storageLimitInMtz, requiredAmountInMtz);

                if (amountInMtz <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, " +
                        "feeAmount: {@feeAmount}, storageLimit: {@storageLimit}, result: {@result}.",
                        walletAddress.Address,
                        balanceInMtz,
                        feeAmountInMtz,
                        storageLimitInMtz,
                        amountInMtz);

                    continue;
                }

                requiredAmountInMtz -= amountInMtz;

                if (isInitTx)
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency     = Xtz,
                        CreationTime = DateTime.UtcNow,
                        From         = walletAddress.Address,
                        To           = Xtz.SwapContractAddress,
                        Amount       = Math.Round(amountInMtz, 0),
                        Fee          = feeAmountInMtz,
                        GasLimit     = Xtz.InitiateGasLimit,
                        StorageLimit = Xtz.InitiateStorageLimit,
                        Params       = InitParams(swap, refundTimeStampUtcInSec, (long)rewardForRedeemInMtz),
                        Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                    });
                }
                else
                {
                    transactions.Add(new TezosTransaction
                    {
                        Currency     = Xtz,
                        CreationTime = DateTime.UtcNow,
                        From         = walletAddress.Address,
                        To           = Xtz.SwapContractAddress,
                        Amount       = Math.Round(amountInMtz, 0),
                        Fee          = feeAmountInMtz,
                        GasLimit     = Xtz.AddGasLimit,
                        StorageLimit = Xtz.AddStorageLimit,
                        Params       = AddParams(swap),
                        Type         = BlockchainTransactionType.Output | BlockchainTransactionType.SwapPayment
                    });
                }

                if (isInitTx)
                    isInitTx = false;

                if (requiredAmountInMtz == 0)
                    break;
            }

            if (requiredAmountInMtz > 0)
            {
                Log.Warning("Insufficient funds (left {@requredAmount}).", requiredAmountInMtz);
                return Enumerable.Empty<TezosTransaction>();
            }

            return transactions;
        }

        private async Task<bool> SignTransactionAsync(
            TezosTransaction tx,
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
            TezosTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var broadcastResult = await Xtz.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

            if (broadcastResult.HasError)
                throw new Exception($"Error while broadcast transaction with code {broadcastResult.Error.Code} and description {broadcastResult.Error.Description}");

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

                var tx = await Xtz.BlockchainApi.GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (!tx.HasError && tx.Value != null && tx.Value.State == BlockchainTransactionState.Confirmed)
                    return true;
            }

            return false;
        }

        private JObject InitParams(
            ClientSwap swap,
            long refundTimestamp,
            long redeemFeeAmount)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Left','args':[{'prim':'Left','args':[{'prim':'Pair','args':[{'string':'" + swap.PartyAddress + "'},{'prim':'Pair','args':[{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'int':'" + refundTimestamp + "'}]},{'int':'" + redeemFeeAmount + "'}]}]}]}]}}");
        }

        private JObject AddParams(ClientSwap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Left','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}}");
        }

        private JObject RedeemParams(ClientSwap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Right','args':[{'prim':'Left','args':[{'bytes':'" + swap.Secret.ToHexString() + "'}]}]}}");
        }

        private JObject RefundParams(ClientSwap swap)
        {
            return JObject.Parse(@"{'entrypoint':'default','value':{'prim':'Right','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}}");
        }

        #endregion Helpers
    }
}