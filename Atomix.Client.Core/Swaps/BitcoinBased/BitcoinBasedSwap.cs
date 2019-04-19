using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Atomix.Blockchain;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Core.Entities;
using Atomix.Swaps.Abstract;
using Atomix.Wallet;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps.BitcoinBased
{
    public class BitcoinBasedSwap : CurrencySwap
    {
        private readonly IBitcoinBasedSwapTransactionFactory _transactionFactory;

        public BitcoinBasedSwap(
            Currency currency,
            SwapState swapState,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            IBitcoinBasedSwapTransactionFactory transactionFactory)
            : base(
                currency,
                swapState,
                account,
                swapClient,
                taskPerformer)
        {
            _transactionFactory = transactionFactory ?? throw new ArgumentNullException(nameof(transactionFactory));
        }

        public override async Task InitiateSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Initiate swap {@swapId}",
                propertyValue: _swapState.Id);

            CreateSecret();
            CreateSecretHash();

            SendData(SwapDataType.SecretHash, _swapState.SecretHash);

            _swapState.PaymentTx = await CreatePaymentTxAsync()
                .ConfigureAwait(false);

            _swapState.PaymentTx = await SignPaymentTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PaymentTx)
                .ConfigureAwait(false);

            _swapState.SetPaymentSigned();

            _swapState.RefundTx = await CreateRefundTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PaymentTx,
                    lockTime: TimeSpan.FromHours(DefaultInitiatorLockTimeHours))
                .ConfigureAwait(false);

            SendTransactionData(SwapDataType.InitiatorPayment, _swapState.PaymentTx);
            SendTransactionData(SwapDataType.InitiatorRefund, _swapState.RefundTx);
        }

        public override async Task AcceptSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Accept swap {@swapId}",
                propertyValue: _swapState.Id);

            _swapState.PaymentTx = await CreatePaymentTxAsync()
                .ConfigureAwait(false);

            _swapState.PaymentTx = await SignPaymentTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PaymentTx)
                .ConfigureAwait(false);

            _swapState.SetPaymentSigned();

            _swapState.RefundTx = await CreateRefundTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PaymentTx,
                    lockTime: TimeSpan.FromHours(DefaultCounterPartyLockTimeHours))
                .ConfigureAwait(false);

            SendTransactionData(SwapDataType.CounterPartyPayment, _swapState.PaymentTx);
            SendTransactionData(SwapDataType.CounterPartyRefund, _swapState.RefundTx);
        }

        public override Task PrepareToReceiveAsync()
        {
            // nothing to do for purchased bitcoin base party
            return Task.CompletedTask;
        }

        public override async Task RestoreSwapAsync()
        {
            if (_swapState.StateFlags.HasFlag(SwapStateFlags.IsRefundBroadcast))
            {
                _taskPerformer.EnqueueTask(new TransactionConfirmedTask
                {
                    Currency = _swapState.Order.SoldCurrency(),
                    SwapState = _swapState,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = _swapState.RefundTx.Id,
                    CompleteHandler = RefundConfirmedEventHandler
                });

                return;
            }

            if (_swapState.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast))
            {
                if (_swapState.PaymentTx == null &&
                    _swapState.PaymentTxId == null)
                {
                    Log.Error(
                        "Error while trying to restore swap with id {@id}. Payment transaction information not saved.",
                        _swapState.Id);
                    return;
                }

                var needSign = !_swapState.StateFlags.HasFlag(SwapStateFlags.IsPaymentConfirmed);

                await PrepareRefundAsync(needSign)
                    .ConfigureAwait(false);

                return;
            }

            // reject swap in others cases
            _swapState.Cancel();
        }

        public override Task HandleSwapData(SwapData swapData)
        {
            switch (swapData.Type)
            {
                case SwapDataType.InitiatorPayment:
                    return HandleInitiatorPaymentTxAsync(swapData.Data);
                case SwapDataType.InitiatorRefund:
                    return HandleInitiatorRefundTxAsync(swapData.Data);
                case SwapDataType.InitiatorRefundSigned:
                    return HandleInitiatorRefundSignedTxAsync(swapData.Data);
                case SwapDataType.InitiatorPaymentTxId:
                    return HandleInitiatorPaymentTxId(Encoding.UTF8.GetString(swapData.Data));
                case SwapDataType.CounterPartyPayment:
                    return HandleCounterPartyPaymentTxAsync(swapData.Data);
                case SwapDataType.CounterPartyRefund:
                    return HandleCounterPartyRefundTxAsync(swapData.Data);
                case SwapDataType.CounterPartyRefundSigned:
                    return HandleCounterPartyRefundSignedTxAsync(swapData.Data);
                case SwapDataType.CounterPartyPaymentTxId:
                    return HandleCounterPartyPaymentTxId(Encoding.UTF8.GetString(swapData.Data));
                default:
                    throw new Exception("Invalid swap data type");
            }
        }

        public override async Task RedeemAsync()
        {
            var currency = _swapState.Order.PurchasedCurrency();

            var redeemAddress = await _account
                .GetFreeInternalAddressAsync(currency)
                .ConfigureAwait(false);

            // create redeem tx
            _swapState.RedeemTx = await CreateRedeemTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PartyPaymentTx,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            // sign redeem tx
            _swapState.RedeemTx = await SignRedeemTxAsync(
                    redeemTx: (IBitcoinBasedTransaction)_swapState.RedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PartyPaymentTx)
                .ConfigureAwait(false);

            _swapState.SetRedeemSigned();

            // broadcast redeem tx
            await BroadcastRedeemAsync(_swapState.RedeemTx)
                .ConfigureAwait(false);

            _swapState.SetRedeemBroadcast();

            // add new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: _swapState.RedeemTx,
                    selfAddresses: new[] { redeemAddress.Address })
                .ConfigureAwait(false);
        }

        public override async Task BroadcastPaymentAsync()
        {
            if (_swapState.IsCounterParty &&
                (!_swapState.StateFlags.HasFlag(SwapStateFlags.HasPartyPayment) ||
                 !_swapState.StateFlags.HasFlag(SwapStateFlags.IsPartyPaymentConfirmed) ||
                 !_swapState.StateFlags.HasFlag(SwapStateFlags.IsRefundSigned)))
            {
                Log.Debug(
                    "CounterParty is not ready to broadcast payment tx for swap {@swap}",
                    _swapState.Id);
                return;
            }

            Log.Debug(
                messageTemplate: "Broadcast payment tx for swap {@swap}",
                propertyValue: _swapState.Id);

            var currency = _swapState.Order.SoldCurrency();

            // broadcast payment transaction
            var txId = await currency.BlockchainApi
                .BroadcastAsync(_swapState.PaymentTx)
                .ConfigureAwait(false);

            _swapState.SetPaymentBroadcast();

            Log.Debug(
                messageTemplate: "Payment txId {@id}",
                propertyValue: txId);

            // account new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: _swapState.PaymentTx,
                    selfAddresses: new[] { _swapState.Order.RefundWallet.Address },
                    notify: false)
                .ConfigureAwait(false);

            if (_swapState.IsInitiator)
            {
                // send payment txId to counterParty
                SendData(SwapDataType.InitiatorPaymentTxId, Encoding.UTF8.GetBytes(txId));
            }
            else
            {
                // send payment txId to initiator
                SendData(SwapDataType.CounterPartyPaymentTxId, Encoding.UTF8.GetBytes(txId));

                var swapOutputs = ((IBitcoinBasedTransaction)_swapState.PaymentTx)
                    .SwapOutputs()
                    .ToList();

                if (swapOutputs.Count != 1)
                    throw new InternalException(
                        code: Errors.SwapError,
                        description: "Payment tx must have only one swap output");

                // track counter party payment spent event
                _taskPerformer.EnqueueTask(new BitcoinBasedOutputSpentTask
                {
                    Currency = currency,
                    SwapState = _swapState,
                    OutputHash = txId,
                    OutputIndex = swapOutputs.First().Index,
                    Interval = DefaultOutputSpentCheckInterval,
                    CompleteHandler = PaymentSpentEventHandler
                });
            }

            // track payment transaction confirmation
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = currency,
                SwapState = _swapState,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = PaymentConfirmedEventHandler
            });
        }

        private async Task HandleInitiatorPaymentTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's payment tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received initiator's payment tx for swap {_swapState.Id}");

            if (_swapState.PartyPaymentTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's payment tx already received for swap {_swapState.Id}");

            var currency = (BitcoinBasedCurrency)_swapState.Order.PurchasedCurrency();

            var paymentTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyPaymentTx(paymentTx, _swapState.Order, out var error))
                throw new InternalException(error);

            Log.Debug(
                "Initiator's payment tx successfully received for swap {@swapId}",
                _swapState.Id);

            _swapState.PartyPaymentTx = paymentTx;

            await SignAndSendPartyRefundAsync()
                .ConfigureAwait(false);
        }

        private async Task HandleInitiatorRefundTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's refund tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received initiator's refund tx for swap {_swapState.Id}");

            if (_swapState.PartyRefundTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's refund tx already received for swap {_swapState.Id}");

            var currency = (BitcoinBasedCurrency)_swapState.Order.PurchasedCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyRefundTx(refundTx, _swapState.Order, out var error))
                throw new InternalException(error);

            Log.Debug(
                "Initiator's refund tx successfully received for swap {@swapId}",
                _swapState.Id);

            _swapState.PartyRefundTx = refundTx;

            await SignAndSendPartyRefundAsync()
                .ConfigureAwait(false);
        }

        private async Task HandleInitiatorRefundSignedTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's refund signed tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received signed initiator's refund tx for swap {_swapState.Id}");

            //if (SwapState.InitiatorRefundSignedTx != null)
            //    throw new InternalException(
            //        code: Errors.WrongSwapMessageOrder,
            //        description: $"Initiator's signed refund tx already received for swap {SwapState.Id}");

            var currency = (BitcoinBasedCurrency)_swapState.Order.SoldCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifySignedRefundTx(refundTx, _swapState.Order, out var error))
                throw new InternalException(error);

            Log.Debug(
                "Initiator's refund tx successfully received for swap {@swapId}",
                _swapState.Id);

            _swapState.RefundTx = refundTx;
            _swapState.SetRefundSigned();

            await BroadcastPaymentAsync()
                .ConfigureAwait(false);
        }

        private async Task HandleInitiatorPaymentTxId(string txId)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's payment txId for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received initiator's payment txId for swap {_swapState.Id}");

            if (_swapState.PartyPaymentTxId != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's payment txId already received for swap {_swapState.Id}");

            _swapState.PartyPaymentTxId = txId;

            // get initiator payment tx from blockchain
            await GetPartyPaymentTxAsync()
                .ConfigureAwait(false);

            // track initiator payment confirmation
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = _swapState.Order.PurchasedCurrency(),
                SwapState = _swapState,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = PartyPaymentConfirmedEventHandler
            });
        }

        private async Task HandleCounterPartyPaymentTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's payment tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received counterParty's payment tx for swap {_swapState.Id}");

            if (_swapState.PartyPaymentTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's payment tx already received for swap {_swapState.Id}");

            var currency = (BitcoinBasedCurrency)_swapState.Order.PurchasedCurrency();

            var paymentTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyPaymentTx(paymentTx, _swapState.Order, out var error))
                throw new InternalException(error);

            Log.Debug(
                "CounterParty's payment tx successfully received for swap {@swap}",
                _swapState.Id);

            _swapState.PartyPaymentTx = paymentTx;

            await SignAndSendPartyRefundAsync()
                .ConfigureAwait(false);
        }

        private async Task HandleCounterPartyRefundTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's refund tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received counterParty's refund tx for swap {_swapState.Id}");

            if (_swapState.PartyRefundTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's refund tx already received for swap {_swapState.Id}");

            var currency = (BitcoinBasedCurrency)_swapState.Order.PurchasedCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyRefundTx(refundTx, _swapState.Order, out var error))
                throw new InternalException(error);

            Log.Debug(
                "CounterParty's refund tx successfully received for swap {@swapId}",
                _swapState.Id);

            _swapState.PartyRefundTx = refundTx;

            await SignAndSendPartyRefundAsync()
                .ConfigureAwait(false);
        }

        private async Task HandleCounterPartyRefundSignedTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's refund signed tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received signed counterParty's refund tx for swap {_swapState.Id}");

            //if (SwapState.CounterPartyRefundSignedTx != null)
            //    throw new InternalException(
            //        code: Errors.WrongSwapMessageOrder,
            //        description: $"CounterParty's signed refund tx already received for swap {SwapState.Id}");

            var currency = (BitcoinBasedCurrency)_swapState.Order.SoldCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifySignedRefundTx(refundTx, _swapState.Order, out var error))
                throw new InternalException(error);

            Log.Debug(
                "CounterParty's refund tx successfully received for swap {@swapId}",
                _swapState.Id);

            _swapState.RefundTx = refundTx;
            _swapState.SetRefundSigned();

            await BroadcastPaymentAsync()
                .ConfigureAwait(false);
        }

        private async Task HandleCounterPartyPaymentTxId(string txId)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's payment txId for swap {@swapId}",
                propertyValue: _swapState.Id);

            if (_swapState.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received counterParty's payment txId for swap {_swapState.Id}");

            if (_swapState.PartyPaymentTxId != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's payment txId already received for swap {_swapState.Id}");

            _swapState.PartyPaymentTxId = txId;

            // get counterParty payment tx from blockchain
            await GetPartyPaymentTxAsync()
                .ConfigureAwait(false);

            // track counterParty payment confirmation
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = _swapState.Order.PurchasedCurrency(),
                SwapState = _swapState,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = PartyPaymentConfirmedEventHandler
            });
        }

        #region Transaction creation

        private async Task<IBitcoinBasedTransaction> CreatePaymentTxAsync()
        {
            var currency = (BitcoinBasedCurrency)_swapState.Order.SoldCurrency();

            Log.Debug(
                messageTemplate: "Create swap payment {@currency} tx for swap {@swapId}",
                propertyValue0: currency.Name,
                propertyValue1: _swapState.Id);

            var tx = await _transactionFactory
                .CreateSwapPaymentTxAsync(
                    currency: currency,
                    order: _swapState.Order,
                    requisites: _swapState.Requisites,
                    secretHash: _swapState.SecretHash,
                    outputsSource: new LocalTxOutputSource(_account))
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "Payment tx successfully created for swap {@swapId}",
                propertyValue: _swapState.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> CreateRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            TimeSpan lockTime)
        {
            Log.Debug(
                messageTemplate: "Create refund tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            var tx = await _transactionFactory
                .CreateSwapRefundTxAsync(
                    paymentTx: paymentTx,
                    order: _swapState.Order,
                    lockTime: DateTimeOffset.UtcNow + lockTime)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Refund tx creation error for swap {_swapState.Id}");

            Log.Debug(
                messageTemplate: "Refund tx successfully created for swap {@swapId}",
                propertyValue: _swapState.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> CreateRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress)
        {
            Log.Debug(
                messageTemplate: "Create redeem tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            var tx = await _transactionFactory
                .CreateSwapRedeemTxAsync(
                    paymentTx: paymentTx,
                    order: _swapState.Order,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Redeem tx creation error for swap {_swapState.Id}");

            return tx;
        }

        #endregion Transaction creation

        #region Transaction signing

        private async Task<IBitcoinBasedTransaction> SignPaymentTxAsync(
            IBitcoinBasedTransaction paymentTx)
        {
            Log.Debug(
                messageTemplate: "Sign payment tx for swap {@swapId}",
                propertyValue: _swapState.Id);

            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignPaymentTxAsync(paymentTx)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Payment tx signing error for swap {_swapState.Id}");

            Log.Debug(
                messageTemplate: "Payment tx successfully signed for swap {@swapId}",
                propertyValue: _swapState.Id);

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> SignRefundTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignRefundTxAsync(
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    order: _swapState.Order)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Refund tx not signed for swap {_swapState.Id}");

            return tx;
        }

        private async Task SignAndSendPartyRefundAsync()
        {
            if (!_swapState.StateFlags.HasFlag(SwapStateFlags.HasPartyRefund) ||
                !_swapState.StateFlags.HasFlag(SwapStateFlags.HasPartyPayment) ||
                _swapState.StateFlags.HasFlag(SwapStateFlags.IsPartyRefundSigned))
            {
                Log.Debug(
                    messageTemplate: "Not ready to sign and send party's refund tx for swap {@swapId}",
                    propertyValue: _swapState.Id);

                return;
            }

            _swapState.PartyRefundTx = await SignRefundTxAsync(
                    refundTx: (IBitcoinBasedTransaction)_swapState.PartyRefundTx,
                    paymentTx: (IBitcoinBasedTransaction)_swapState.PartyPaymentTx)
                .ConfigureAwait(false);

            _swapState.SetPartyRefundSigned();

            var dataType = _swapState.IsInitiator
                ? SwapDataType.CounterPartyRefundSigned
                : SwapDataType.InitiatorRefundSigned;

            SendTransactionData(dataType, _swapState.PartyRefundTx);
        }

        private async Task<IBitcoinBasedTransaction> SignRedeemTxAsync(
            IBitcoinBasedTransaction redeemTx,
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignRedeemTxAsync(
                    redeemTx: redeemTx,
                    paymentTx: paymentTx,
                    order: _swapState.Order,
                    secret: _swapState.Secret)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Redeem tx sign error for swap {_swapState.Id}");

            return tx;
        }

        private async Task<IBitcoinBasedTransaction> SignSelfRefundTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignSelfRefundTxAsync(
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    order: _swapState.Order)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: "Self refund signing error");

            return tx;
        }

        #endregion Transaction signing

        private IBitcoinBasedTransaction ParseTransaction(BitcoinBasedCurrency currency, byte[] transactionBytes)
        {
            if (!BitcoinBasedTransactionParser.TryParseTransaction(currency, transactionBytes, out var tx))
                throw new InternalException(
                    code: Errors.WrongSwapData,
                    description: $"Can't parse tx for swap {_swapState.Id}");

            return tx;
        }

        private async Task BroadcastRedeemAsync(IBlockchainTransaction redeemTx)
        {
            var currency = _swapState.Order.PurchasedCurrency();

            var txId = await currency.BlockchainApi
                .BroadcastAsync(redeemTx)
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "Redeem tx {@txId} successfully broadcast for swap {@swapId}",
                propertyValue0: txId,
                propertyValue1: _swapState.Id);
        }

        private async void PaymentConfirmedEventHandler(BackgroundTask task)
        {
            var confirmedTask = task as TransactionConfirmedTask;
            var swap = confirmedTask?.SwapState;

            if (swap == null)
                return;

            Log.Debug(
                messageTemplate: "Handle payment confirmed event for swap {@swapId}",
                propertyValue: swap.Id);

            swap.SetPaymentConfirmed();

            try
            {
                if (confirmedTask.Tx != null)
                    await _account
                        .AddConfirmedTransactionAsync(confirmedTask.Tx)
                        .ConfigureAwait(false);

                await PrepareRefundAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle payment tx confirmed event");
            }

            if (_swapState.IsInitiator)
                RaiseInitiatorPaymentConfirmed(_swapState);
            else
                RaiseCounterPartyPaymentConfirmed(_swapState);
        }

        private async void PartyPaymentConfirmedEventHandler(BackgroundTask task)
        {
            var confirmedTask = task as TransactionConfirmedTask;
            var swap = confirmedTask?.SwapState;

            if (swap == null)
                return;

            Log.Debug(
                messageTemplate: "Handle party's payment confirmed event for swap {@swapId}",
                propertyValue: swap.Id);

            swap.SetPartyPaymentConfirmed();

            try
            {
                if (_swapState.IsInitiator)
                    await RedeemAsync()
                        .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle counterParty's payment tx confirmed event");
            }

            if (_swapState.IsInitiator)
                RaiseCounterPartyPaymentConfirmed(_swapState);
            else
                RaiseInitiatorPaymentConfirmed(_swapState);
        }

        private async Task PrepareRefundAsync(bool selfSign = true)
        {
            if (selfSign)
            {
                _swapState.RefundTx = await SignSelfRefundTxAsync(
                        refundTx: (IBitcoinBasedTransaction)_swapState.RefundTx,
                        paymentTx: (IBitcoinBasedTransaction)_swapState.PaymentTx)
                    .ConfigureAwait(false);
            }

            _taskPerformer.EnqueueTask(new RefundTimeControlTask
            {
                Currency = _swapState.Order.SoldCurrency(),
                SwapState = _swapState,
                Interval = DefaultRefundInterval,
                RefundTimeUtc = ((IBitcoinBasedTransaction)_swapState.RefundTx).LockTime,
                CompleteHandler = RefundTimeControlEventHandler
            });
        }

        private async void RefundTimeControlEventHandler(BackgroundTask task)
        {
            try
            {
                var refundTx = (IBitcoinBasedTransaction)_swapState.RefundTx;

                var txId = await _currency.BlockchainApi
                    .BroadcastAsync(refundTx)
                    .ConfigureAwait(false);

                Log.Debug("Refund tx id {@txId} for swap {@swapId}", txId, _swapState.Id);

                // todo: check result

                _swapState.SetRefundBroadcast();

                await _account
                    .AddUnconfirmedTransactionAsync(
                        tx: refundTx,
                        selfAddresses: new[] { _swapState.Order.RefundWallet.Address })
                    .ConfigureAwait(false);

                _taskPerformer.EnqueueTask(new TransactionConfirmedTask
                {
                    Currency = _swapState.Order.SoldCurrency(),
                    SwapState = _swapState,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = _swapState.RefundTx.Id,
                    CompleteHandler = RefundConfirmedEventHandler
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund task error");
            }
        }

        private void RefundConfirmedEventHandler(BackgroundTask task)
        {
            _swapState.SetRefundConfirmed();
        }

        private async void PaymentSpentEventHandler(BackgroundTask task)
        {
            var spentTask = task as BitcoinBasedOutputSpentTask;
            var swap = spentTask?.SwapState;

            if (swap == null)
                return;

            Log.Debug("Handle payment spent event for swap {@swapId}", swap.Id);

            try
            {
                if (spentTask.SpentPoint == null)
                    throw new InternalException(
                        code: Errors.InvalidSpentPoint,
                        description: "Invalid spent point");

                // extract secret
                await GetSecretAsync(spentTask.SpentPoint)
                    .ConfigureAwait(false);

                RaiseCounterPartyPaymentSpent(_swapState);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle payment tx spent event");
            }
        }

        private async Task<IBitcoinBasedTransaction> GetPaymentTxAsync(
            BitcoinBasedCurrency currency,
            string txId)
        {
            var attempts = 0;

            while (attempts < DefaultGetTransactionAttempts)
            {
                attempts++;

                var tx = (IBitcoinBasedTransaction)await currency.BlockchainApi
                    .GetTransactionAsync(txId)
                    .ConfigureAwait(false);

                if (tx != null)
                    return tx;

                await Task.Delay(DefaultGetTransactionInterval)
                    .ConfigureAwait(false);
            }

            throw new InternalException(
                code: Errors.SwapError,
                description: $"Transaction with id {txId} not found");
        }

        private async Task GetPartyPaymentTxAsync()
        {
            var currency = (BitcoinBasedCurrency)_swapState.Order.PurchasedCurrency();

            var tx = await GetPaymentTxAsync(currency, _swapState.PartyPaymentTxId)
                .ConfigureAwait(false);

            if (!TransactionVerifier.TryVerifyPaymentTx(tx, _swapState.Order, out var error))
                throw new InternalException(error);

            _swapState.PartyPaymentTx = tx;
        }

        private async Task GetSecretAsync(ITxPoint spentPoint)
        {
            Log.Debug(
                messageTemplate: "Try to get CounterParty's payment spent output {@hash}:{@no} for swap {@swapId}",
                propertyValue0: spentPoint.Hash,
                propertyValue1: spentPoint.Index,
                propertyValue2: _swapState.Id);

            var soldCurrency = _swapState.Order.SoldCurrency();

            var swapInput = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                .GetInputAsync(spentPoint.Hash, spentPoint.Index)
                .ConfigureAwait(false);

            var secret = swapInput.ExtractSecret();
            var secretHash = CreateSwapSecretHash(secret);

            if (!secretHash.SequenceEqual(_swapState.SecretHash))
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: "Invalid secret hash");

            _swapState.Secret = secret;
        }

        private void SendTransactionData(
            SwapDataType dataType,
            IBlockchainTransaction tx)
        {
            SendData(dataType, ((IBitcoinBasedTransaction)tx).ToBytes());
        }
    }
}