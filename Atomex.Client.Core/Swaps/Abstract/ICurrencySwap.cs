using System.Threading;
using System.Threading.Tasks;
using Atomex.Core;

namespace Atomex.Swaps.Abstract
{
    public delegate void OnSwapUpdatedDelegate(ICurrencySwap currencySwap, SwapEventArgs swapArgs);

    public interface ICurrencySwap
    {
        OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate AcceptorPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate AcceptorPaymentSpent { get; set; }
        OnSwapUpdatedDelegate SwapUpdated { get; set; }

        string Currency { get; }

        /// <summary>
        /// Broadcast payment transaction(s) for currency being sold
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task PayAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Start to control party payment transaction confirmation
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task StartPartyPaymentControlAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems swap for currency being purchased
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task RedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems swap for party
        /// </summary>
        /// <returns>Task</returns>
        Task RedeemForPartyAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refund swap for currency begin sold
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task RefundAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Start to wait redeem for swap
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task StartWaitForRedeemAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Start to wait for redeem for swap for currency being purchased in case when acceptor doesn't have funds to redeem for himself
        /// </summary>
        /// <returns>Task</returns>
        Task StartWaitForRedeemBySomeoneAsync(
            Swap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handle party payment tx
        /// </summary>
        /// <param name="swap">Local swap</param>
        /// <param name="clientSwap">Received swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task HandlePartyPaymentAsync(
            Swap swap,
            Swap clientSwap,
            CancellationToken cancellationToken = default);
    }
}