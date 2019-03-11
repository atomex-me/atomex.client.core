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
    public class BitcoinBasedSwapProtocol : SwapProtocol
    {
        public BitcoinBasedSwapProtocol(
            Currency currency,
            Swap swap,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            OnSwapUpdatedDelegate onSwapUpdated = null)
            : base(
                currency,
                swap,
                account,
                swapClient,
                taskPerformer,
                onSwapUpdated)
        {
        }

        public override async Task InitiateSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Initiate swap {@swapId}",
                propertyValue: _swap.Id);

            CreateSecret();
            CreateSecretHash();

            SendData(SwapDataType.SecretHash, _swap.SecretHash);

            await CreateInitiatorPaymentTxAsync()
                .ConfigureAwait(false);

            await SignInitiatorPaymentTxAsync()
                .ConfigureAwait(false);

            await CreateInitiatorRefundTxAsync()
                .ConfigureAwait(false);

            SendTransactionData(SwapDataType.InitiatorPayment, _swap.InitiatorPaymentTx);
            SendTransactionData(SwapDataType.InitiatorRefund, _swap.InitiatorRefundTx);
        }

        public override async Task AcceptSwapAsync()
        {
            Log.Debug(
                messageTemplate: "Accept swap {@swapId}",
                propertyValue: _swap.Id);

            if (_currency.Name.Equals(_swap.Order.PurchasedCurrency().Name))
            {
                // nothing to do for purchased bitcoin base party
                return;       
            }

            await CreateCounterPartyPaymentTxAsync()
                .ConfigureAwait(false);

            await SignCounterPartyPaymentTxAsync()
                .ConfigureAwait(false);

            await CreateCounterPartyRefundTxAsync()
                .ConfigureAwait(false);

            SendTransactionData(SwapDataType.CounterPartyPayment, _swap.CounterPartyPaymentTx);
            SendTransactionData(SwapDataType.CounterPartyRefund, _swap.CounterPartyRefundTx);
        }

        public override async Task RestoreSwapAsync()
        {
            if (_swap.IsInitiator)
            {
                //if (_swap.State.HasFlag(SwapState.IsCounterPartyRedeemBroadcast)) { }
                if (_swap.State.HasFlag(SwapState.IsInitiatorRefundBroadcast))
                {
                    _taskPerformer.EnqueueTask(new TransactionConfirmedTask
                    {
                        Currency = _swap.Order.SoldCurrency(),
                        Swap = _swap,
                        Interval = DefaultConfirmationCheckInterval,
                        TxId = _swap.InitiatorRefundSignedTx.Id,
                        CompleteHandler = InitiatorRefundConfirmedEventHandler
                    });

                    return;
                }

                if (_swap.State.HasFlag(SwapState.IsInitiatorPaymentBroadcast))
                {
                    if (_swap.InitiatorPaymentSignedTx == null &&
                        _swap.InitiatorPaymentTxId == null)
                    {
                        Log.Error("Error while trying to restore swap with id {@id}. Payment transaction information not saved.", _swap.Id);
                        return;
                    }

                    var needSign = !_swap.State.HasFlag(SwapState.IsInitiatorPaymentConfirmed);

                    await PrepareInitiatorRefundAsync(needSign)
                        .ConfigureAwait(false);

                    return;
                }
            }
            else
            {
                //if (_swap.State.HasFlag(SwapState.IsInitiatorRedeemBroadcast)) { }
                if (_swap.State.HasFlag(SwapState.IsCounterPartyRefundBroadcast))
                {
                    _taskPerformer.EnqueueTask(new TransactionConfirmedTask
                    {
                        Currency = _swap.Order.SoldCurrency(),
                        Swap = _swap,
                        Interval = DefaultConfirmationCheckInterval,
                        TxId = _swap.CounterPartyRefundSignedTx.Id,
                        CompleteHandler = CounterPartyRefundConfirmedEventHandler
                    });

                    return;
                }

                if (_swap.State.HasFlag(SwapState.IsCounterPartyPaymentBroadcast))
                {
                    if (_swap.CounterPartyPaymentSignedTx == null &&
                        _swap.CounterPartyPaymentTxId == null)
                    {
                        Log.Error("Error while trying to restore swap with id {@id}. Payment transaction information not saved.", _swap.Id);
                        return;
                    }

                    var needSign = !_swap.State.HasFlag(SwapState.IsCounterPartyPaymentConfirmed);

                    await PrepareCounterPartyRefundAsync(needSign)
                        .ConfigureAwait(false);

                    return;
                }
            }

            // reject swap in others cases
            _swap.Reject();
            RaiseSwapUpdated(_swap);
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
                    return base.HandleSwapData(swapData);
            }
        }

        public override Task RedeemAsync()
        {
            if (_swap.IsInitiator) {
                return RedeemByInitiatorAsync();
            } else {
                return RedeemByCounterPartyAsync();
            }
        }

        public override Task BroadcastPayment()
        {
            if (_swap.IsInitiator) {
                return BroadcastInitiatorPaymentAsync();
            } else {
                return BroadcastCounterPartyPaymentAsync();
            }
        }

        public async Task HandleInitiatorPaymentTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's payment tx for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received initiator's payment tx for swap {_swap.Id}");

            if (_swap.InitiatorPaymentTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's payment tx already received for swap {_swap.Id}");

            var currency = (BitcoinBasedCurrency)_swap.Order.PurchasedCurrency();

            var paymentTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyPaymentTx(paymentTx, _swap.Order, out var error))
                throw new InternalException(error);

            Log.Debug("Initiator's payment tx successfully received");

            _swap.SetInitiatorPaymentTx(paymentTx);
            RaiseSwapUpdated(_swap);

            await SignAndSendInitiatorRefundAsync()
                .ConfigureAwait(false);
        }

        public async Task HandleInitiatorRefundTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's refund tx for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received initiator's refund tx for swap {_swap.Id}");

            if (_swap.InitiatorRefundTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's refund tx already received for swap {_swap.Id}");

            var currency = (BitcoinBasedCurrency)_swap.Order.PurchasedCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyRefundTx(refundTx, _swap.Order, out var error))
                throw new InternalException(error);

            Log.Debug("Initiator's refund tx successfully received");

            _swap.SetInitiatorRefundTx(refundTx);
            RaiseSwapUpdated(_swap);

            await SignAndSendInitiatorRefundAsync()
                .ConfigureAwait(false);
        }

        public async Task HandleInitiatorRefundSignedTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's refund signed tx for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received signed initiator's refund tx for swap {_swap.Id}");

            if (_swap.InitiatorRefundSignedTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's signed refund tx already received for swap {_swap.Id}");

            var currency = (BitcoinBasedCurrency) _swap.Order.SoldCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifySignedRefundTx(refundTx, _swap.Order, out var error))
                throw new InternalException(error);

            Log.Debug("Initiator's refund tx successfully received");

            _swap.SetInitiatorRefundSignedTx(refundTx);
            RaiseSwapUpdated(_swap);

            await BroadcastInitiatorPaymentAsync()
                .ConfigureAwait(false);
        }

        public async Task HandleInitiatorPaymentTxId(string txId)
        {
            Log.Debug(
                messageTemplate: "Handle initiator's payment txId for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received initiator's payment txId for swap {_swap.Id}");

            if (_swap.InitiatorPaymentTxId != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator's payment txId already received for swap {_swap.Id}");

            _swap.SetInitiatorPaymentTxId(txId);
            RaiseSwapUpdated(_swap);

            // get initiator payment tx from blockchain
            await GetInitiatorPaymentTxAsync()
                .ConfigureAwait(false);

            // track initiator payment confirmation
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = _swap.Order.PurchasedCurrency(),
                Swap = _swap,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = InitiatorPaymentConfirmedEventHandler
            });
        }

        public async Task HandleCounterPartyPaymentTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's payment tx for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received counterParty's payment tx for swap {_swap.Id}");

            if (_swap.CounterPartyPaymentTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's payment tx already received for swap {_swap.Id}");

            var currency = (BitcoinBasedCurrency) _swap.Order.PurchasedCurrency();

            var paymentTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyPaymentTx(paymentTx, _swap.Order, out var error))
                throw new InternalException(error);

            Log.Debug("CounterParty's payment tx successfully received");

            _swap.SetCounterPartyPaymentTx(paymentTx);
            RaiseSwapUpdated(_swap);

            await SignAndSendCounterPartyRefundAsync()
                .ConfigureAwait(false);
        }

        public async Task HandleCounterPartyRefundTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's refund tx for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received counterParty's refund tx for swap {_swap.Id}");

            if (_swap.CounterPartyRefundTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's refund tx already received for swap {_swap.Id}");

            var currency = (BitcoinBasedCurrency) _swap.Order.PurchasedCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifyRefundTx(refundTx, _swap.Order, out var error))
                throw new InternalException(error);

            Log.Debug("CounterParty's refund tx successfully received");

            _swap.SetCounterPartyRefundTx(refundTx);
            RaiseSwapUpdated(_swap);

            await SignAndSendCounterPartyRefundAsync()
                .ConfigureAwait(false);
        }

        public async Task HandleCounterPartyRefundSignedTxAsync(byte[] transactionBytes)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's refund signed tx for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received signed counterParty's refund tx for swap {_swap.Id}");

            if (_swap.CounterPartyRefundSignedTx != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's signed refund tx already received for swap {_swap.Id}");

            var currency = (BitcoinBasedCurrency) _swap.Order.SoldCurrency();

            var refundTx = ParseTransaction(currency, transactionBytes);

            if (!TransactionVerifier.TryVerifySignedRefundTx(refundTx, _swap.Order, out var error))
                throw new InternalException(error);

            Log.Debug("Initiator's refund tx successfully received");

            _swap.SetCounterPartyRefundSignedTx(refundTx);
            RaiseSwapUpdated(_swap);

            await BroadcastCounterPartyPaymentAsync()
                .ConfigureAwait(false);
        }

        public async Task HandleCounterPartyPaymentTxId(string txId)
        {
            Log.Debug(
                messageTemplate: "Handle counterParty's payment txId for swap {@swapId}",
                propertyValue: _swap.Id);

            if (_swap.IsCounterParty)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty received counterParty's payment txId for swap {_swap.Id}");

            if (_swap.CounterPartyPaymentTxId != null)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"CounterParty's payment txId already received for swap {_swap.Id}");

            _swap.SetCounterPartyPaymentTxId(txId);
            RaiseSwapUpdated(_swap);

            // get counterParty payment tx from blockchain
            await GetCounterPartyPaymentTxAsync()
                .ConfigureAwait(false);

            // track initiator payment confirmation
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = _swap.Order.PurchasedCurrency(),
                Swap = _swap,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = CounterPartyPaymentConfirmedEventHandler
            });
        }

        #region Transaction creation

        private async Task<IBitcoinBasedTransaction> CreatePaymentTxAsync()
        {
            var currency = (BitcoinBasedCurrency)_swap.Order.SoldCurrency();

            Log.Debug(
                messageTemplate: "Create swap payment {@currency} tx for swap {@swapId}",
                propertyValue0: currency.Name,
                propertyValue1: _swap.Id);

            var tx = await BitcoinBasedSwapTransactionFactory
                .CreateSwapPaymentTxAsync(
                    currency: currency,
                    order: _swap.Order,
                    requisites: _swap.Requisites,
                    secretHash: _swap.SecretHash,
                    outputsSource: new LocalTxOutputSource(_account))
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "Payment tx successfully created for swap {@swapId}",
                propertyValue: _swap.Id);

            return tx;
        }

        private async Task CreateInitiatorPaymentTxAsync()
        {
            var tx = await CreatePaymentTxAsync()
                .ConfigureAwait(false);

            _swap.SetInitiatorPaymentTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task CreateCounterPartyPaymentTxAsync()
        {
            var tx = await CreatePaymentTxAsync()
                .ConfigureAwait(false);

            _swap.SetCounterPartyPaymentTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task<IBitcoinBasedTransaction> CreateRefundTxAsync(
            IBitcoinBasedTransaction paymentTx,
            TimeSpan lockTime)
        {
            Log.Debug(
                messageTemplate: "Create refund tx for swap {@swapId}",
                propertyValue: _swap.Id);

            var tx = await paymentTx
                .CreateSwapRefundTxAsync(
                    order: _swap.Order,
                    lockTime: DateTimeOffset.UtcNow + lockTime)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Refund tx creation error for swap {_swap.Id}");

            Log.Debug(
                messageTemplate: "Refund tx successfully created for swap {@swapId}",
                propertyValue: _swap.Id);

            return tx;
        }

        private async Task CreateInitiatorRefundTxAsync()
        {
            var tx = await CreateRefundTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swap.InitiatorPaymentSignedTx,
                    lockTime: TimeSpan.FromHours(DefaultInitiatorLockTimeHours))
                .ConfigureAwait(false);

            _swap.SetInitiatorRefundTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task CreateCounterPartyRefundTxAsync()
        {
            var tx = await CreateRefundTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swap.CounterPartyPaymentSignedTx,
                    lockTime: TimeSpan.FromHours(DefaultCounterPartyLockTimeHours))
                .ConfigureAwait(false);

            _swap.SetCounterPartyRefundTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task<IBitcoinBasedTransaction> CreateRedeemTxAsync(
            IBitcoinBasedTransaction paymentTx,
            WalletAddress redeemAddress)
        {
            Log.Debug(
                messageTemplate: "Create redeem tx for swap {@swapId}",
                propertyValue: _swap.Id);

            var tx = await paymentTx
                .CreateSwapRedeemTxAsync(
                    order: _swap.Order,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionCreationError,
                    description: $"Redeem tx creation error for swap {_swap.Id}");

            return tx;
        }

        private async Task CreateInitiatorRedeemTxAsync(WalletAddress redeemAddress)
        {
            var tx = await CreateRedeemTxAsync(
                    paymentTx: (IBitcoinBasedTransaction)_swap.InitiatorPaymentSignedTx,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            _swap.SetInitiatorRedeemTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task CreateCounterPartyRedeemTxAsync(WalletAddress redeemAddress)
        {
            var tx = await CreateRedeemTxAsync(
                    paymentTx:(IBitcoinBasedTransaction)_swap.CounterPartyPaymentSignedTx,
                    redeemAddress: redeemAddress)
                .ConfigureAwait(false);

            _swap.SetCounterPartyRedeemTx(tx);
            RaiseSwapUpdated(_swap);
        }

        #endregion Transaction creation

        #region Transaction signing

        private async Task<IBitcoinBasedTransaction> SignPaymentTxAsync(
            IBitcoinBasedTransaction paymentTx)
        {
            Log.Debug(
                messageTemplate: "Sign payment tx for swap {@swapId}",
                propertyValue: _swap.Id);

            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignPaymentTxAsync(paymentTx, _swap.Order)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Payment tx signing error for swap {_swap.Id}");

            Log.Debug(
                messageTemplate: "Payment tx successfully signed for swap {@swapId}",
                propertyValue: _swap.Id);

            return tx;
        }

        private async Task SignInitiatorPaymentTxAsync()
        {
            var tx = await SignPaymentTxAsync((IBitcoinBasedTransaction)_swap.InitiatorPaymentTx)
                .ConfigureAwait(false);

            _swap.SetInitiatorPaymentSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task SignCounterPartyPaymentTxAsync()
        {
            var tx = await SignPaymentTxAsync((IBitcoinBasedTransaction)_swap.CounterPartyPaymentTx)
                .ConfigureAwait(false);

            _swap.SetCounterPartyPaymentSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task<IBitcoinBasedTransaction> SignRefundTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignRefundTxAsync(
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    order: _swap.Order)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Refund tx not signed for swap {_swap.Id}");

            return tx;
        }

        private async Task SignAndSendInitiatorRefundAsync()
        {
            if (!_swap.State.HasFlag(SwapState.HasInitiatorRefund) ||
                !_swap.State.HasFlag(SwapState.HasInitiatorPayment) ||
                _swap.State.HasFlag(SwapState.HasInitiatorRefundSigned))
            {
                Log.Debug(
                    messageTemplate: "CounterParty not ready to sign and send initiator's refund tx for swap {@swapId}",
                    propertyValue: _swap.Id);

                return;
            }

            var refundTx = await SignRefundTxAsync(
                    refundTx: (IBitcoinBasedTransaction)_swap.InitiatorRefundTx,
                    paymentTx: (IBitcoinBasedTransaction)_swap.InitiatorPaymentTx)
                .ConfigureAwait(false);

            _swap.SetInitiatorRefundSignedTx(refundTx);
            RaiseSwapUpdated(_swap);

            SendTransactionData(SwapDataType.InitiatorRefundSigned, refundTx);
        }

        private async Task SignAndSendCounterPartyRefundAsync()
        {
            if (!_swap.State.HasFlag(SwapState.HasCounterPartyRefund) ||
                !_swap.State.HasFlag(SwapState.HasCounterPartyPayment) ||
                _swap.State.HasFlag(SwapState.HasCounterPartyRefundSigned))
            {
                Log.Debug(
                    messageTemplate: "Initiator not ready to sign and send counterParty's refund tx for swap {@swapId}",
                    propertyValue: _swap.Id);

                return;
            }

            var refundTx = await SignRefundTxAsync(
                    refundTx: (IBitcoinBasedTransaction)_swap.CounterPartyRefundTx,
                    paymentTx: (IBitcoinBasedTransaction)_swap.CounterPartyPaymentTx)
                .ConfigureAwait(false);

            _swap.SetCounterPartyRefundSignedTx(refundTx);
            RaiseSwapUpdated(_swap);

            SendTransactionData(SwapDataType.CounterPartyRefundSigned, refundTx);
        }

        private async Task<IBitcoinBasedTransaction> SignRedeemTxAsync(
            IBitcoinBasedTransaction redeemTx,
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignRedeemTxAsync(
                    redeemTx: redeemTx,
                    paymentTx: paymentTx,
                    order: _swap.Order,
                    secret: _swap.Secret)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: $"Redeem tx sign error for swap {_swap.Id}");

            return tx;
        }

        private async Task SignInitiatorRedeemTxAsync()
        {
            var tx = await SignRedeemTxAsync(
                    redeemTx: (IBitcoinBasedTransaction)_swap.InitiatorRedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)_swap.InitiatorPaymentSignedTx)
                .ConfigureAwait(false);

            _swap.SetInitiatorRedeemSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task SignCounterPartyRedeemTxAsync()
        {
            var tx = await SignRedeemTxAsync(
                    redeemTx: (IBitcoinBasedTransaction)_swap.CounterPartyRedeemTx,
                    paymentTx: (IBitcoinBasedTransaction)_swap.CounterPartyPaymentSignedTx)
                .ConfigureAwait(false);

            _swap.SetCounterPartyRedeemSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task<IBitcoinBasedTransaction> SignSelfRefundTxAsync(
            IBitcoinBasedTransaction refundTx,
            IBitcoinBasedTransaction paymentTx)
        {
            var tx = await new BitcoinBasedSwapSigner(_account)
                .SignSelfRefundTxAsync(
                    refundTx: refundTx,
                    paymentTx: paymentTx,
                    order: _swap.Order)
                .ConfigureAwait(false);

            if (tx == null)
                throw new InternalException(
                    code: Errors.TransactionSigningError,
                    description: "Self refund signing error");

            return tx;
        }

        private async Task SignSelfInitiatorRefundTxAsync()
        {
            var tx = await SignSelfRefundTxAsync(
                    refundTx: (IBitcoinBasedTransaction)_swap.InitiatorRefundSignedTx,
                    paymentTx: (IBitcoinBasedTransaction)_swap.InitiatorPaymentSignedTx)
                .ConfigureAwait(false);

            _swap.SetInitiatorRefundSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task SignSelfCounterPartyRefundTxAsync()
        {
            var tx = await SignSelfRefundTxAsync(
                    refundTx: (IBitcoinBasedTransaction)_swap.CounterPartyRefundSignedTx,
                    paymentTx: (IBitcoinBasedTransaction)_swap.CounterPartyPaymentSignedTx)
                .ConfigureAwait(false);

            _swap.SetCounterPartyRefundSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        #endregion Transaction signing

        private IBitcoinBasedTransaction ParseTransaction(BitcoinBasedCurrency currency, byte[] transactionBytes)
        {
            if (!BitcoinBasedTransactionParser.TryParseTransaction(currency, transactionBytes, out var tx))
                throw new InternalException(
                    code: Errors.WrongSwapData,
                    description: $"Can't parse tx for swap {_swap.Id}");

            return tx;
        }

        private async Task BroadcastInitiatorPaymentAsync()
        {
            Log.Debug(
                messageTemplate: "Broadcast initiator's payment tx for swap {@swap}",
                propertyValue: _swap.Id);

            var currency = _swap.Order.SoldCurrency();

            // broadcast payment transaction
            var txId = await currency.BlockchainApi
                .BroadcastAsync(_swap.InitiatorPaymentSignedTx)
                .ConfigureAwait(false);

            _swap.SetInitiatorPaymentBroadcast();
            RaiseSwapUpdated(_swap);

            Log.Debug(
                messageTemplate: "Initiator payment txId {@id}",
                propertyValue: txId);

            // account new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: _swap.InitiatorPaymentSignedTx,
                    selfAddresses: new[] {_swap.Order.RefundWallet.Address},
                    notify: false)
                .ConfigureAwait(false);

            // send payment txId to counterParty
            SendData(SwapDataType.InitiatorPaymentTxId, Encoding.UTF8.GetBytes(txId));

            // track payment transaction confirmation
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = currency,
                Swap = _swap,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = InitiatorPaymentConfirmedEventHandler
            });
        }

        private async Task BroadcastCounterPartyPaymentAsync()
        {
            if (!_swap.State.HasFlag(SwapState.HasInitiatorPaymentSigned) ||
                !_swap.State.HasFlag(SwapState.IsInitiatorPaymentConfirmed) ||
                !_swap.State.HasFlag(SwapState.HasCounterPartyRefundSigned))
            {
                Log.Debug("CounterParty is not ready to broadcast payment tx");
                return;
            }

            Log.Debug(
                messageTemplate: "Broadcast counterParty's payment tx for swap {@swap}",
                propertyValue: _swap.Id);

            var currency = _swap.Order.SoldCurrency();

            // broadcast payment transaction
            var txId = await currency.BlockchainApi
                .BroadcastAsync(_swap.CounterPartyPaymentSignedTx)
                .ConfigureAwait(false);

            _swap.SetCounterPartyPaymentBroadcast();
            RaiseSwapUpdated(_swap);

            Log.Debug(
                messageTemplate: "CounterParty payment txId {@id}",
                propertyValue: txId);

            // account new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: _swap.CounterPartyPaymentSignedTx,
                    selfAddresses: new[] {_swap.Order.RefundWallet.Address},
                    notify: false)
                .ConfigureAwait(false);

            // send payment txId to initiator
            SendData(SwapDataType.CounterPartyPaymentTxId, Encoding.UTF8.GetBytes(txId));

            var swapOutputs = ((IBitcoinBasedTransaction)_swap.CounterPartyPaymentSignedTx)
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
                Swap = _swap,
                OutputHash = txId,
                OutputIndex = swapOutputs.First().Index,
                Interval = DefaultOutputSpentCheckInterval,
                CompleteHandler = CounterPartyPaymentSpentEventHandler
            });

            // track counter party payment confirmed event
            _taskPerformer.EnqueueTask(new TransactionConfirmedTask
            {
                Currency = currency,
                Swap = _swap,
                TxId = txId,
                Interval = DefaultConfirmationCheckInterval,
                CompleteHandler = CounterPartyPaymentConfirmedEventHandler
            });
        }

        private async Task RedeemByInitiatorAsync()
        {
            var currency = _swap.Order.PurchasedCurrency();

            var redeemAddress = await _account
                .GetFreeInternalAddressAsync(currency)
                .ConfigureAwait(false);

            // create redeem tx
            await CreateCounterPartyRedeemTxAsync(redeemAddress)
                .ConfigureAwait(false);

            // sign redeem tx
            await SignCounterPartyRedeemTxAsync()
                .ConfigureAwait(false);

            // broadcast redeem tx
            await BroadcastCounterPartyRedeemAsync()
                .ConfigureAwait(false);

            // add new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: _swap.CounterPartyRedeemSignedTx,
                    selfAddresses: new[] {redeemAddress.Address})
                .ConfigureAwait(false);
        }

        private async Task RedeemByCounterPartyAsync()
        {
            var currency = _swap.Order.PurchasedCurrency();

            var redeemAddress = await _account
                .GetFreeInternalAddressAsync(currency)
                .ConfigureAwait(false);

            // create redeem tx
            await CreateInitiatorRedeemTxAsync(redeemAddress)
                .ConfigureAwait(false);

            // sign redeem tx
            await SignInitiatorRedeemTxAsync()
                .ConfigureAwait(false);

            // broadcast redeem tx
            await BroadcastInitiatorRedeemAsync()
                .ConfigureAwait(false);

            // add new unconfirmed transaction
            await _account
                .AddUnconfirmedTransactionAsync(
                    tx: _swap.InitiatorRedeemSignedTx,
                    selfAddresses: new[] {redeemAddress.Address})
                .ConfigureAwait(false);
        }

        private async Task BroadcastInitiatorRedeemAsync()
        {
            var currency = _swap.Order.PurchasedCurrency();

            var txId = await currency.BlockchainApi
                .BroadcastAsync(_swap.InitiatorRedeemSignedTx)
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "Initiator's redeem tx {@txId} successfully broadcast",
                propertyValue: txId);

            _swap.SetInitiatorRedeemBroadcast();
            RaiseSwapUpdated(_swap);
        }

        private async Task BroadcastCounterPartyRedeemAsync()
        {
            var currency = _swap.Order.PurchasedCurrency();

            var txId = await currency.BlockchainApi
                .BroadcastAsync(_swap.CounterPartyRedeemSignedTx)
                .ConfigureAwait(false);

            Log.Debug(
                messageTemplate: "CounterParty's redeem tx {@txId} successfully broadcast",
                propertyValue: txId);

            _swap.SetCounterPartyRedeemBroadcast();
            RaiseSwapUpdated(_swap);
        }

        private async void InitiatorPaymentConfirmedEventHandler(BackgroundTask task)
        {
            var confirmedTask = task as TransactionConfirmedTask;
            var swap = confirmedTask?.Swap;

            if (swap == null)
                return;

            Log.Debug(
                messageTemplate: "Handle Initiator's payment confirmed event for swap {@swapId}",
                propertyValue: swap.Id);

            swap.SetInitiatorPaymentConfirmed();
            RaiseSwapUpdated(_swap);

            try
            {
                //if (swap.IsCounterParty)
                //{
                    //await BroadcastCounterPartyPaymentAsync()
                    //    .ConfigureAwait(false);
                //}
                //else
                if (swap.IsInitiator)
                {
                    if (confirmedTask.Tx != null)
                        await _account
                            .AddConfirmedTransactionAsync(confirmedTask.Tx)
                            .ConfigureAwait(false);

                    await PrepareInitiatorRefundAsync()
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle initiator's payment tx confirmed event");
            }

            RaiseInitiatorPaymentConfirmed(_swap);
        }

        private async void CounterPartyPaymentConfirmedEventHandler(BackgroundTask task)
        {
            var confirmedTask = task as TransactionConfirmedTask;
            var swap = confirmedTask?.Swap;

            if (swap == null)
                return;

            Log.Debug(
                messageTemplate: "Handle CounterParty's payment confirmed event for swap {@swapId}",
                propertyValue: swap.Id);

            swap.SetCounterPartyPaymentConfirmed();
            RaiseSwapUpdated(_swap);

            try
            {
                if (_swap.IsInitiator)
                {
                    await RedeemByInitiatorAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    if (confirmedTask.Tx != null)
                        await _account
                            .AddConfirmedTransactionAsync(confirmedTask.Tx)
                            .ConfigureAwait(false);

                    await PrepareCounterPartyRefundAsync()
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle counterParty's payment tx confirmed event");
            }

            RaiseCounterPartyPaymentConfirmed(_swap);
        }

        private async Task PrepareInitiatorRefundAsync(bool selfSign = true)
        {
            if (selfSign)
                await SignSelfInitiatorRefundTxAsync()
                    .ConfigureAwait(false);

            _taskPerformer.EnqueueTask(new RefundTimeControlTask
            {
                Currency = _swap.Order.SoldCurrency(),
                Swap = _swap,
                Interval = DefaultRefundInterval,
                RefundTimeUtc = ((IBitcoinBasedTransaction)_swap.InitiatorRefundSignedTx).LockTime,
                CompleteHandler = InitiatorRefundTimeControlEventHandler
            });
        }

        private async Task PrepareCounterPartyRefundAsync(bool selfSign = true)
        {
            if (selfSign)
                await SignSelfCounterPartyRefundTxAsync()
                    .ConfigureAwait(false);

            _taskPerformer.EnqueueTask(new RefundTimeControlTask
            {
                Currency = _swap.Order.SoldCurrency(),
                Swap = _swap,
                Interval = DefaultRefundInterval,
                RefundTimeUtc = ((IBitcoinBasedTransaction)_swap.CounterPartyRefundSignedTx).LockTime,
                CompleteHandler = CounterPartyRefundTimeControlEventHandler
            });
        }

        private async void InitiatorRefundTimeControlEventHandler(BackgroundTask task)
        {
            try
            {
                var refundTx = (IBitcoinBasedTransaction) _swap.InitiatorRefundSignedTx;

                var txId = await _currency.BlockchainApi
                    .BroadcastAsync(refundTx)
                    .ConfigureAwait(false);

                // todo: check result

                _swap.SetInitiatorRefundBroadcast();
                RaiseSwapUpdated(_swap);

                await _account
                    .AddUnconfirmedTransactionAsync(
                        tx: refundTx,
                        selfAddresses: new[] {_swap.Order.RefundWallet.Address})
                    .ConfigureAwait(false);

                _taskPerformer.EnqueueTask(new TransactionConfirmedTask
                {
                    Currency = _swap.Order.SoldCurrency(),
                    Swap = _swap,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = _swap.InitiatorRefundSignedTx.Id,
                    CompleteHandler = InitiatorRefundConfirmedEventHandler
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund task error");
            }
        }

        private void InitiatorRefundConfirmedEventHandler(BackgroundTask task)
        {
            _swap.SetInitiatorRefundConfirmed();
            RaiseSwapUpdated(_swap);
        }

        private async void CounterPartyRefundTimeControlEventHandler(BackgroundTask task)
        {
            try
            {
                var refundTx = (IBitcoinBasedTransaction)_swap.CounterPartyRefundSignedTx;

                var txId = await _currency.BlockchainApi
                    .BroadcastAsync(refundTx)
                    .ConfigureAwait(false);

                // todo: check result

                _swap.SetCounterPartyRefundBroadcast();
                RaiseSwapUpdated(_swap);

                await _account
                    .AddUnconfirmedTransactionAsync(
                        tx: refundTx,
                        selfAddresses: new[] { _swap.Order.RefundWallet.Address })
                    .ConfigureAwait(false);

                _taskPerformer.EnqueueTask(new TransactionConfirmedTask
                {
                    Currency = _swap.Order.SoldCurrency(),
                    Swap = _swap,
                    Interval = DefaultConfirmationCheckInterval,
                    TxId = _swap.CounterPartyRefundSignedTx.Id,
                    CompleteHandler = CounterPartyRefundConfirmedEventHandler
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Refund task error");
            }
        }

        private void CounterPartyRefundConfirmedEventHandler(BackgroundTask task)
        {
            _swap.SetCounterPartyRefundConfirmed();
            RaiseSwapUpdated(_swap);
        }

        private async void CounterPartyPaymentSpentEventHandler(BackgroundTask task)
        {
            var spentTask = task as BitcoinBasedOutputSpentTask;
            var swap = spentTask?.Swap;

            if (swap == null)
                return;

            Log.Debug("Handle CounterParty's payment spent event");

            try
            {
                if (spentTask.SpentPoint == null)
                    throw new InternalException(
                        code: Errors.InvalidSpentPoint,
                        description: "Invalid spent point");

                // extract secret
                await GetSecretAsync(spentTask.SpentPoint)
                    .ConfigureAwait(false);

                RaiseCounterPartyPaymentSpent(_swap);
                //await RedeemByCounterPartyAsync()
                //    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while handle counterParty's payment tx spent event");
            }
        }

        private async Task<IBitcoinBasedTransaction> GetPaymentTxAsync(BitcoinBasedCurrency currency, string txId)
        {
            var attempts = 0;

            while (attempts < DefaultGetTransactionAttempts)
            {
                attempts++;

                var tx = (IBitcoinBasedTransaction) await currency.BlockchainApi
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

        private async Task GetInitiatorPaymentTxAsync()
        {
            var currency = (BitcoinBasedCurrency)_swap.Order.PurchasedCurrency();

            var tx = await GetPaymentTxAsync(currency, _swap.InitiatorPaymentTxId)
                .ConfigureAwait(false);

            if (!TransactionVerifier.TryVerifyPaymentTx(tx, _swap.Order, out var error))
                throw new InternalException(error);

            _swap.SetInitiatorPaymentSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task GetCounterPartyPaymentTxAsync()
        {
            var currency = (BitcoinBasedCurrency) _swap.Order.PurchasedCurrency();

            var tx = await GetPaymentTxAsync(currency, _swap.CounterPartyPaymentTxId)
                .ConfigureAwait(false);

            if (!TransactionVerifier.TryVerifyPaymentTx(tx, _swap.Order, out var error))
                throw new InternalException(error);

            _swap.SetCounterPartyPaymentSignedTx(tx);
            RaiseSwapUpdated(_swap);
        }

        private async Task GetSecretAsync(ITxPoint spentPoint)
        {
            Log.Debug(
                messageTemplate: "Try to get CounterParty's payment spent output {@hash}:{@no}",
                propertyValue0: spentPoint.Hash,
                propertyValue1: spentPoint.Index);

            var soldCurrency = _swap.Order.SoldCurrency();

            var swapInput = await ((IInOutBlockchainApi)soldCurrency.BlockchainApi)
                .GetInputAsync(spentPoint.Hash, spentPoint.Index)
                .ConfigureAwait(false);

            var secret = swapInput.ExtractSecret();
            var secretHash = CreateSwapSecretHash(secret);

            if (!secretHash.SequenceEqual(_swap.SecretHash))
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: "Invalid secret hash");

            _swap.SetSecret(secret);
            RaiseSwapUpdated(_swap);
        }
    }
}