using System;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Common.Abstract;
using Atomix.Swaps.Abstract;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Swaps
{
    public class UniversalSwapProtocol : SwapProtocol
    {
        private ISwapProtocol _soldCurrencyProtocol;
        private ISwapProtocol _purchasedCurrencyProtocol;

        public UniversalSwapProtocol(        
            Swap swap,
            IAccount account,
            ISwapClient swapClient,
            IBackgroundTaskPerformer taskPerformer,
            OnSwapUpdatedDelegate onSwapUpdated = null)
            : base(
                null,
                swap,
                account,
                swapClient,
                taskPerformer,
                onSwapUpdated)
        {
            var soldCurrency = swap.Order.SoldCurrency();

            _soldCurrencyProtocol = SwapProtocolCreator.Create(
                currency: soldCurrency,
                swap: swap,
                account: account,
                swapClient: swapClient,
                taskPerformer: taskPerformer,
                onSwapUpdated: onSwapUpdated);

            var purchasedCurrency = swap.Order.PurchasedCurrency();

            _purchasedCurrencyProtocol = SwapProtocolCreator.Create(
                currency: purchasedCurrency,
                swap: swap,
                account: account,
                swapClient: swapClient,
                taskPerformer: taskPerformer,
                onSwapUpdated: onSwapUpdated);

            _soldCurrencyProtocol.CounterPartyPaymentSpent = async (sender, args) =>
            {
                try
                {
                    // redeem by counter party async (using purchased currency protocol)
                    if (_swap.IsCounterParty)
                        await _purchasedCurrencyProtocol
                            .RedeemAsync()
                            .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Counter party payment spent handler error");
                }
            };

            _purchasedCurrencyProtocol.InitiatorPaymentConfirmed = async (sender, args) =>
            {
                try
                {
                    // broadcast counter party payment tx (using sold currency protocol)
                    if (swap.IsCounterParty)
                        await _soldCurrencyProtocol
                            .BroadcastPayment()
                            .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Initiator payment confirmed handler error");
                }
            };
        }

        public override Task InitiateSwapAsync()
        {
            return _soldCurrencyProtocol
                .InitiateSwapAsync();
        }

        public override async Task AcceptSwapAsync()
        {
            await _soldCurrencyProtocol
                .AcceptSwapAsync()
                .ConfigureAwait(false);

            await _purchasedCurrencyProtocol
                .AcceptSwapAsync()
                .ConfigureAwait(false);
        }

        public override async Task RestoreSwapAsync()
        {
            await _soldCurrencyProtocol
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
                    return _purchasedCurrencyProtocol.HandleSwapData(swapData);
                case SwapDataType.InitiatorRefundSigned:
                case SwapDataType.CounterPartyRefundSigned:
                    return _soldCurrencyProtocol.HandleSwapData(swapData);
                default:
                    return base.HandleSwapData(swapData);
            }
        }

        public override Task RedeemAsync()
        {
            return _purchasedCurrencyProtocol.RedeemAsync();
        }

        public override Task BroadcastPayment()
        {
            return _soldCurrencyProtocol.BroadcastPayment();
        }
    }
}