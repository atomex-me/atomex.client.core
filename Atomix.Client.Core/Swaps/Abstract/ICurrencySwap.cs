using System.Threading.Tasks;

namespace Atomix.Swaps.Abstract
{
    public delegate void OnSwapUpdatedDelegate(object sender, SwapEventArgs swapArgs);

    public interface ICurrencySwap : ISwap
    {
        OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate CounterPartyPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate CounterPartyPaymentSpent { get; set; }

        /// <summary>
        /// Prepating to receive the purchased currency
        /// </summary>
        /// <returns></returns>
        Task PrepareToReceiveAsync();

        /// <summary>
        /// Redeems swap for currency being purchased
        /// </summary>
        /// <returns></returns>
        Task RedeemAsync();

        /// <summary>
        /// Broadcast payment transactions for currency being sold
        /// </summary>
        /// <returns></returns>
        Task BroadcastPaymentAsync();
    }
}