using System.Threading.Tasks;
using Atomex.Core.Entities;

namespace Atomex.Swaps.Abstract
{
    public delegate void OnSwapUpdatedDelegate(ICurrencySwap currencySwap, SwapEventArgs swapArgs);

    public interface ICurrencySwap
    {
        OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate AcceptorPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate AcceptorPaymentSpent { get; set; }
        OnSwapUpdatedDelegate SwapUpdated { get; set; }

        Currency Currency { get; }

        /// <summary>
        /// Broadcast payment transactions for currency being sold
        /// </summary>
        /// <returns></returns>
        Task BroadcastPaymentAsync(ClientSwap swap);

        /// <summary>
        /// Preparing to receive the purchased currency
        /// </summary>
        /// <returns></returns>
        Task PrepareToReceiveAsync(ClientSwap swap);

        /// <summary>
        /// Redeems swap for currency being purchased
        /// </summary>
        /// <returns></returns>
        Task RedeemAsync(ClientSwap swap);

        /// <summary>
        /// Waits for redeem for swap for currency being purchased in case when counterparty doesn't have funds to redeem for himself
        /// </summary>
        /// <returns></returns>
        Task WaitForRedeemAsync(ClientSwap swap);

        /// <summary>
        /// Redeems swap for party
        /// </summary>
        /// <returns></returns>
        Task PartyRedeemAsync(ClientSwap swap);

        /// <summary>
        /// Restores swap
        /// </summary>
        /// <returns></returns>
        Task RestoreSwapAsync(ClientSwap swap);

        /// <summary>
        /// Handle party payment tx
        /// </summary>
        /// <param name="swap">Local swap</param>
        /// <param name="clientSwap">Received swap</param>
        /// <returns></returns>
        Task HandlePartyPaymentAsync(ClientSwap swap, ClientSwap clientSwap);
    }
}