using System.Threading;
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
        /// Broadcast payment transaction(s) for currency being sold
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task PayAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Preparing to receive the purchased currency
        /// </summary>
        /// <returns></returns>
        Task PrepareToReceiveAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems swap for currency being purchased
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refund swap for currency begin sold
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits for redeem for swap for currency being purchased in case when acceptor doesn't have funds to redeem for himself
        /// </summary>
        /// <returns></returns>
        Task WaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems swap for party
        /// </summary>
        /// <returns></returns>
        Task PartyRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores swap for sold currency
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task RestoreSwapForSoldCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores swap for purchased currency
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task RestoreSwapForPurchasedCurrencyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handle party payment tx
        /// </summary>
        /// <param name="swap">Local swap</param>
        /// <param name="clientSwap">Received swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task HandlePartyPaymentAsync(
            ClientSwap swap,
            ClientSwap clientSwap,
            CancellationToken cancellationToken = default);
    }
}