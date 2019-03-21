using System;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps
{
    public class UniversalSwap : Swap
    {
        private ISwap _soldCurrencySwap;
        private ISwap _purchasedCurrencySwap;

        public UniversalSwap(        
            SwapState swapState,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer)
            : base(
                null,
                swapState,
                account,
                swapClient,
                taskPerformer)
        {
            var soldCurrency = swapState.Order.SoldCurrency();

            _soldCurrencySwap = SwapProtocolCreator.Create(
                currency: soldCurrency,
                swapState: swapState,
                account: account,
                swapClient: swapClient,
                taskPerformer: taskPerformer);

            var purchasedCurrency = swapState.Order.PurchasedCurrency();

            _purchasedCurrencySwap = SwapProtocolCreator.Create(
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

        public override async Task InitiateSwapAsync()
        {
            await _soldCurrencySwap
                .InitiateSwapAsync()
                .ConfigureAwait(false);

            await _purchasedCurrencySwap
                .InitiateSwapAsync()
                .ConfigureAwait(false);
        }

        public override async Task AcceptSwapAsync()
        {
            await _soldCurrencySwap
                .AcceptSwapAsync()
                .ConfigureAwait(false);

            await _purchasedCurrencySwap
                .AcceptSwapAsync()
                .ConfigureAwait(false);
        }

        public override async Task RestoreSwapAsync()
        {
            await _soldCurrencySwap
                .RestoreSwapAsync()
                .ConfigureAwait(false);
        }

        public override Task HandleSwapData(SwapData swapData)
        {
            switch (swapData.Type)
            {
                case SwapDataType.InitiatorPayment:
                case SwapDataType.InitiatorRefund:
                case SwapDataType.InitiatorPaymentTxId:
                case SwapDataType.CounterPartyPayment:
                case SwapDataType.CounterPartyRefund:
                case SwapDataType.CounterPartyPaymentTxId:
                    return _purchasedCurrencySwap.HandleSwapData(swapData);
                case SwapDataType.InitiatorRefundSigned:
                case SwapDataType.CounterPartyRefundSigned:
                    return _soldCurrencySwap.HandleSwapData(swapData);
                default:
                    return base.HandleSwapData(swapData);
            }
        }

        public override Task RedeemAsync()
        {
            return _purchasedCurrencySwap.RedeemAsync();
        }

        public override Task BroadcastPaymentAsync()
        {
            return _soldCurrencySwap.BroadcastPaymentAsync();
        }
    }
}