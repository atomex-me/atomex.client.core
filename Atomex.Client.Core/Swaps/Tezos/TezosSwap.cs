using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Atomex.Common.Abstract;
using Atomex.Core;
using Atomex.Core.Entities;
using Atomex.Swaps.Abstract;
using Atomex.Swaps.Tasks;
using Atomex.Swaps.Tezos.Tasks;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Swaps.Tezos
{
    public class TezosSwap : CurrencySwap
    {
        public TezosSwap(
            Currency currency,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
            : base(
                currency,
                account,
                swapClient,
                taskPerformer)
        {
        }

        private Atomex.Tezos Xtz => (Atomex.Tezos)Currency;

        public override async Task BroadcastPaymentAsync(ClientSwap swap)
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
                    swap.SetPaymentSigned();
                    RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentSigned);
                }

                await BroadcastTxAsync(swap, tx)
                        .ConfigureAwait(false);

                if (isInitiateTx)
                {
                    swap.PaymentTx = tx;
                    swap.SetPaymentBroadcast();
                    RaiseSwapUpdated(swap, SwapStateFlags.IsPaymentBroadcast);

                    isInitiateTx = false;

                    // delay for contract initiation
                    if (txs.Count > 1)
                        await Task.Delay(TimeSpan.FromSeconds(60))
                            .ConfigureAwait(false);
                }
            }

            // start redeem control
            TaskPerformer.EnqueueTask(new TezosSwapRedeemControlTask
            {
                Currency = Currency,
                RefundTimeUtc = swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                Swap = swap,
                CompleteHandler = RedeemControlCompletedEventHandler,
                CancelHandler = RedeemControlCanceledEventHandler
            });
        }

        public override Task PrepareToReceiveAsync(ClientSwap swap)
        {
            // initiator waits "accepted" event, acceptor waits "initiated" event
            var handler = swap.IsInitiator
                ? SwapAcceptedEventHandler
                : (OnTaskDelegate)SwapInitiatedEventHandler;

            var lockTimeSeconds = swap.IsInitiator
                ? DefaultAcceptorLockTimeInSeconds
                : DefaultInitiatorLockTimeInSeconds;

            var refundTimeStampUtcInSec = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeSeconds)).ToUnixTimeSeconds();

            TaskPerformer.EnqueueTask(new TezosSwapInitiatedControlTask
            {
                Currency = Currency,
                Swap = swap,
                Interval = DefaultConfirmationCheckInterval,
                RefundTimestamp = refundTimeStampUtcInSec,
                CompleteHandler = handler,
                CancelHandler = SwapCanceledEventHandler
            });

            return Task.CompletedTask;
        }

        public override Task RestoreSwapAsync(ClientSwap swap)
        {
            return swap.IsSoldCurrency(Currency)
                ? RestoreForSoldCurrencyAsync(swap)
                : RestoreForPurchasedCurrencyAsync(swap);
        }

        public override async Task RedeemAsync(ClientSwap swap)
        {
            Log.Debug("Create redeem for swap {@swapId}", swap.Id);

            var walletAddress = (await Account.GetUnspentAddressesAsync(
                    currency: Currency,
                    amount: 0,
                    fee: Xtz.RedeemFee.ToTez() + Xtz.RedeemStorageLimit.ToTez(),
                    feePrice: 0,
                    isFeePerTransaction: true,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress)
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

            var signResult = await SignTransactionAsync(redeemTx)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            swap.RedeemTx = redeemTx;
            swap.SetRedeemSigned();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemSigned);

            await BroadcastTxAsync(swap, redeemTx)
                .ConfigureAwait(false);

            swap.RedeemTx = redeemTx;
            swap.SetRedeemBroadcast();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemBroadcast);

            TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
            {
                Currency = Currency,
                Swap = swap,
                Interval = DefaultConfirmationCheckInterval,
                TxId = redeemTx.Id,
                CompleteHandler = RedeemConfirmedEventHandler
            });
        }

        public override Task WaitForRedeemAsync(ClientSwap swap)
        {
            Log.Debug("Wait redeem for swap {@swapId}", swap.Id);

            // start redeem control
            TaskPerformer.EnqueueTask(new TezosSwapRedeemControlTask
            {
                Currency = Currency,
                RefundTimeUtc = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                Swap = swap,
                CompleteHandler = RedeemPartyControlCompletedEventHandler,
                CancelHandler = RedeemPartyControlCanceledEventHandler
            });

            return Task.CompletedTask;
        }

        public override async Task PartyRedeemAsync(ClientSwap swap)
        {
            Log.Debug("Create redeem for acceptor for swap {@swapId}", swap.Id);

            var walletAddress = (await Account
                .GetUnspentAddressesAsync(
                    currency: Currency,
                    amount: 0,
                    fee: Xtz.RedeemFee.ToTez() + Xtz.RedeemStorageLimit.ToTez(),
                    feePrice: 0,
                    isFeePerTransaction: false,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress)
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

            var signResult = await SignTransactionAsync(redeemTx)
                .ConfigureAwait(false);

            if (!signResult)
            {
                Log.Error("Transaction signing error");
                return;
            }

            await BroadcastTxAsync(swap, redeemTx)
                .ConfigureAwait(false);
        }

        private async Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Log.Debug("Create refund for swap {@swap}", swap.Id);

            var walletAddress = (await Account.GetUnspentAddressesAsync(
                    currency: Currency,
                    amount: 0,
                    fee: Xtz.RefundFee.ToTez() + Xtz.RefundStorageLimit.ToTez(),
                    feePrice: 0,
                    isFeePerTransaction: true,
                    addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress,
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
            swap.SetRefundSigned();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundSigned);

            await BroadcastTxAsync(swap, refundTx, cancellationToken)
                .ConfigureAwait(false);

            swap.RefundTx = refundTx;
            swap.SetRefundBroadcast();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundBroadcast);

            TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
            {
                Currency = Currency,
                Swap = swap,
                Interval = DefaultConfirmationCheckInterval,
                TxId = refundTx.Id,
                CompleteHandler = RefundConfirmedEventHandler
            });
        }

        private Task RestoreForSoldCurrencyAsync(ClientSwap swap)
        {
            if (swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemSigned))
                    return Task.CompletedTask; // we already have redeem, let's check it in RestoreForPurchasedCurrency

                if (!(swap.PaymentTx is TezosTransaction))
                {
                    Log.Error("Can't restore swap {@id}. Payment tx is null.", swap.Id);
                    return Task.CompletedTask;
                }

                var lockTimeInSeconds = swap.IsInitiator
                    ? DefaultInitiatorLockTimeInSeconds
                    : DefaultAcceptorLockTimeInSeconds;

                // start redeem control
                TaskPerformer.EnqueueTask(new TezosSwapRedeemControlTask
                {
                    Currency = Currency,
                    RefundTimeUtc = swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSeconds),
                    Swap = swap,
                    CompleteHandler = RedeemControlCompletedEventHandler,
                    CancelHandler = RedeemControlCanceledEventHandler
                });
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

        private async Task RestoreForPurchasedCurrencyAsync(ClientSwap swap)
        {
            if (swap.RewardForRedeem > 0 &&
                swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                // may be swap already redeemed by someone else, let's check it
                await WaitForRedeemAsync(swap)
                    .ConfigureAwait(false);
            }
            else if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemSigned) &&
                     !swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast))
            {
                // redeem tx created, signed, but not broadcast.
                // there is a possibility that tx could successfully broadcast
                // othersise try again

                TaskPerformer.EnqueueTask(new TezosSwapRedeemControlTask
                {
                    Currency = Currency,
                    RefundTimeUtc = swap.TimeStamp.ToUniversalTime().AddSeconds(DefaultAcceptorLockTimeInSeconds),
                    Swap = swap,
                    CancelOnlyWhenRefundTimeReached = false,
                    CompleteHandler = RedeemPartyControlCompletedEventHandler,
                    CancelHandler = RedeemPartyControlCanceledEventHandler
                });
            }
            else if (swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemBroadcast) &&
                    !swap.StateFlags.HasFlag(SwapStateFlags.IsRedeemConfirmed))
            {
                // redeem broadcast, but not confirmed

                if (!(swap.RedeemTx is TezosTransaction redeemTx))
                {
                    Log.Error("Can't restore swap {@id}. Redeem tx is null", swap.Id);
                    return;
                }

                TaskPerformer.EnqueueTask(new TransactionConfirmationCheckTask
                {
                    Currency = Currency,
                    Swap = swap,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = redeemTx.Id,
                    CompleteHandler = RedeemConfirmedEventHandler
                });
            }
        }

        #region Event Handlers

        private void SwapInitiatedEventHandler(BackgroundTask task)
        {
            var initiatedControlTask = task as TezosSwapInitiatedControlTask;
            var swap = initiatedControlTask?.Swap;

            if (swap == null)
                return;

            Log.Debug(
                "Initiator's payment transaction received. Now acceptor can broadcast payment tx for swap {@swapId}",
                swap.Id);

            swap.SetHasPartyPayment();
            swap.SetPartyPaymentConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

            InitiatorPaymentConfirmed?.Invoke(this, new SwapEventArgs(swap));
        }

        private async void SwapAcceptedEventHandler(BackgroundTask task)
        {
            var initiatedControlTask = task as TezosSwapInitiatedControlTask;
            var swap = initiatedControlTask?.Swap;

            if (swap == null)
                return;

            try
            {
                Log.Debug(
                    "Acceptor's payment transaction received. Now initiator can do self redeem and do party redeem for acceptor (if needs and wants) for swap {@swapId}.",
                    swap.Id);

                swap.SetHasPartyPayment();
                swap.SetPartyPaymentConfirmed();
                RaiseSwapUpdated(swap, SwapStateFlags.HasPartyPayment | SwapStateFlags.IsPartyPaymentConfirmed);

                RaiseAcceptorPaymentConfirmed(swap);

                await RedeemAsync(swap)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap accepted error");
            }
        }

        private void SwapCanceledEventHandler(BackgroundTask task)
        {
            var initiatedControlTask = task as TezosSwapInitiatedControlTask;
            var swap = initiatedControlTask?.Swap;

            if (swap == null)
                return;

            // todo: do smth here
            Log.Debug("Swap canceled due to wrong counterParty params {@swapId}", swap.Id);
        }

        private void RedeemConfirmedEventHandler(BackgroundTask task)
        {
            var confirmationCheckTask = task as TransactionConfirmationCheckTask;
            var swap = confirmationCheckTask?.Swap;

            swap?.SetRedeemConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);
        }

        private void RedeemControlCompletedEventHandler(BackgroundTask task)
        {
            var redeemControlTask = task as TezosSwapRedeemControlTask;
            var swap = redeemControlTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle redeem control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = redeemControlTask.Secret;
                RaiseSwapUpdated(swap, SwapStateFlags.HasSecret);

                RaiseAcceptorPaymentSpent(swap);
            }
        }

        private void RedeemControlCanceledEventHandler(BackgroundTask task)
        {
            var redeemControlTask = task as TezosSwapRedeemControlTask;
            var swap = redeemControlTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle redeem control canceled event for swap {@swapId}", swap.Id);

            TaskPerformer.EnqueueTask(new RefundTimeControlTask
            {
                Currency = Currency,
                RefundTimeUtc = redeemControlTask.RefundTimeUtc,
                Swap = swap,
                CompleteHandler = RefundTimeReachedEventHandler
            });
        }

        private void RefundTimeReachedEventHandler(BackgroundTask task)
        {
            var refundTimeControlTask = task as RefundTimeControlTask;
            var swap = refundTimeControlTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Refund time reached for swap {@swapId}", swap.Id);

            TaskPerformer.EnqueueTask(new TezosSwapRefundControlTask
            {
                Currency = Currency,
                Swap = swap,
                CompleteHandler = RefundConfirmedEventHandler,
                CancelHandler = RefundEventHandler
            });
        }

        private async void RefundEventHandler(BackgroundTask task)
        {
            var refundControlTask = task as TezosSwapRefundControlTask;
            var swap = refundControlTask?.Swap;

            if (swap == null)
                return;

            try
            {
                await RefundAsync(swap)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund error");
            }
        }

        private void RefundConfirmedEventHandler(BackgroundTask task)
        {
            var confirmationCheckTask = task as TransactionConfirmationCheckTask;
            var swap = confirmationCheckTask?.Swap;

            swap?.SetRefundConfirmed();
            RaiseSwapUpdated(swap, SwapStateFlags.IsRefundConfirmed);
        }

        private void RedeemPartyControlCompletedEventHandler(BackgroundTask task)
        {
            var redeemControlTask = task as TezosSwapRedeemControlTask;
            var swap = redeemControlTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle redeem party control completed event for swap {@swapId}", swap.Id);

            if (swap.IsAcceptor)
            {
                swap.Secret = redeemControlTask.Secret;
                swap.SetRedeemConfirmed();
                RaiseSwapUpdated(swap, SwapStateFlags.IsRedeemConfirmed);

                // get transactions & update balance for address
                TaskPerformer.EnqueueTask(new AddressBalanceUpdateTask
                {
                    Account = Account,
                    Address = swap.ToAddress,
                    Currency = Currency,
                });
            }
        }

        private async void RedeemPartyControlCanceledEventHandler(BackgroundTask task)
        {
            var redeemControlTask = task as TezosSwapRedeemControlTask;
            var swap = redeemControlTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle redeem party control canceled event for swap {@swapId}", swap.Id);

            try
            {
                if (swap.Secret?.Length > 0)
                {
                    var walletAddress = (await Account.GetUnspentAddressesAsync(
                            currency: Currency,
                            amount: 0,
                            fee: Xtz.RedeemFee.ToTez() + Xtz.RedeemStorageLimit.ToTez(),
                            feePrice: 0,
                            isFeePerTransaction: true,
                            addressUsagePolicy: AddressUsagePolicy.UseOnlyOneAddress)
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

                    await RedeemAsync(swap)
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
            CancellationToken cancellationToken = default(CancellationToken))
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
                .SortList((a, b) => a.AvailableBalance().CompareTo(b.AvailableBalance()));

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

                var paidStorageDiffInMtz = isInitTx
                    ? Xtz.InitiatePaidStorageDiff
                    : Xtz.AddPaidStorageDiff;

                var amountInMtz = Math.Min(balanceInMtz - feeAmountInMtz - paidStorageDiffInMtz, requiredAmountInMtz);

                if (amountInMtz <= 0)
                {
                    Log.Warning(
                        "Insufficient funds at {@address}. Balance: {@balance}, " +
                        "feeAmount: {@feeAmount}, paidStorageDiffAmount: {@paidStorageDiffAmount}, result: {@result}.",
                        walletAddress.Address,
                        balanceInMtz,
                        feeAmountInMtz,
                        paidStorageDiffInMtz,
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
            CancellationToken cancellationToken = default(CancellationToken))
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
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var txId = await Xtz.BlockchainApi
                .BroadcastAsync(tx, cancellationToken)
                .ConfigureAwait(false);

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

        private JObject InitParams(
            ClientSwap swap,
            long refundTimestamp,
            long redeemFeeAmount)
        {
            return JObject.Parse(@"{'prim':'Left','args':[{'prim':'Left','args':[{'prim':'Pair','args':[{'string':'" + swap.PartyAddress + "'},{'prim':'Pair','args':[{'prim':'Pair','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'},{'int':'" + refundTimestamp + "'}]},{'int':'" + redeemFeeAmount + "'}]}]}]}]}");
        }

        private JObject AddParams(ClientSwap swap)
        {
            return JObject.Parse(@"{'prim':'Left','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}");
        }

        private JObject RedeemParams(ClientSwap swap)
        {
            return JObject.Parse(@"{'prim':'Right','args':[{'prim':'Left','args':[{'bytes':'" + swap.Secret.ToHexString() + "'}]}]}");
        }

        private JObject RefundParams(ClientSwap swap)
        {
            return JObject.Parse(@"{'prim':'Right','args':[{'prim':'Right','args':[{'bytes':'" + swap.SecretHash.ToHexString() + "'}]}]}");
        }

        #endregion Helpers
    }
}