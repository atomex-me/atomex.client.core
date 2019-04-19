using System;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Core;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps
{
    public class Swap : ISwap
    {
        private SwapState _swapState;
        private ICurrencySwap _soldCurrencySwap;
        private ICurrencySwap _purchasedCurrencySwap;

        public Swap(        
            SwapState swapState,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
        {
            _swapState = swapState;

            var soldCurrency = swapState.Order.SoldCurrency();

            _soldCurrencySwap = CurrencySwapCreator.Create(
                currency: soldCurrency,
                swapState: swapState,
                account: account,
                swapClient: swapClient,
                taskPerformer: taskPerformer);

            var purchasedCurrency = swapState.Order.PurchasedCurrency();

            _purchasedCurrencySwap = CurrencySwapCreator.Create(
                currency: purchasedCurrency,
                swapState: swapState,
                account: account,
                swapClient: swapClient,
                taskPerformer: taskPerformer);

            _soldCurrencySwap.CounterPartyPaymentSpent = async (sender, args) =>
            {
                try
                {
                    // redeem by counter party async (using purchased currency protocol)
                    if (_swapState.IsCounterParty)
                        await _purchasedCurrencySwap
                            .RedeemAsync()
                            .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Counter party payment spent handler error");
                }
            };

            _purchasedCurrencySwap.InitiatorPaymentConfirmed = async (sender, args) =>
            {
                try
                {
                    // broadcast counter party payment tx (using sold currency protocol)
                    if (swapState.IsCounterParty)
                        await _soldCurrencySwap
                            .BroadcastPaymentAsync()
                            .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Initiator payment confirmed handler error");
                }
            };
        }

        public async Task InitiateSwapAsync()
        {
            await _soldCurrencySwap
                .InitiateSwapAsync()
                .ConfigureAwait(false);

            await _purchasedCurrencySwap
                .PrepareToReceiveAsync()
                .ConfigureAwait(false);
        }

        public async Task AcceptSwapAsync()
        {
            await _soldCurrencySwap
                .AcceptSwapAsync()
                .ConfigureAwait(false);

            await _purchasedCurrencySwap
                .PrepareToReceiveAsync()
                .ConfigureAwait(false);
        }

        public async Task RestoreSwapAsync()
        {
            await _soldCurrencySwap
                .RestoreSwapAsync()
                .ConfigureAwait(false);
        }

        public Task HandleSwapData(SwapData swapData)
        {
            switch (swapData.Type)
            {
                case SwapDataType.SecretHash:
                    return HandleSecretHashAsync(swapData.Data);

                case SwapDataType.InitiatorPayment:
                case SwapDataType.InitiatorRefund:
                case SwapDataType.InitiatorPaymentTxId:
                case SwapDataType.CounterPartyPayment:
                case SwapDataType.CounterPartyRefund:
                case SwapDataType.CounterPartyPaymentTxId:
                    return _purchasedCurrencySwap
                        .HandleSwapData(swapData);

                case SwapDataType.InitiatorRefundSigned:
                case SwapDataType.CounterPartyRefundSigned:
                    return _soldCurrencySwap
                        .HandleSwapData(swapData);
                default:
                    throw new ArgumentOutOfRangeException(nameof(swapData.Type));
            }
        }

        protected async Task HandleSecretHashAsync(byte[] secretHash)
        {
            Log.Debug(
                messageTemplate: "Handle secret hash {@hash} for swap {@swapId}",
                propertyValue0: secretHash?.ToHexString(),
                propertyValue1: _swapState.Id);

            AcceptSecretHash(secretHash);

            await AcceptSwapAsync()
                .ConfigureAwait(false);
        }

        protected void AcceptSecretHash(byte[] secretHash)
        {
            if (_swapState.IsInitiator)
                throw new InternalException(
                    code: Errors.WrongSwapMessageOrder,
                    description: $"Initiator received secret hash message for swap {_swapState.Id}");

            if (secretHash == null || secretHash.Length != CurrencySwap.DefaultSecretHashSize)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Incorrect secret hash length for swap {_swapState.Id}");

            if (_swapState.SecretHash != null)
                throw new InternalException(
                    code: Errors.InvalidSecretHash,
                    description: $"Secret hash already received for swap {_swapState.Id}");

            Log.Debug(
                messageTemplate: "Secret hash {@hash} successfully received",
                propertyValue: secretHash.ToHexString());

            _swapState.SecretHash = secretHash;
        }
    }
}