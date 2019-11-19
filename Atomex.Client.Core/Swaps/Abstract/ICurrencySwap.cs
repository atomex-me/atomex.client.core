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
        /// <returns>Task</returns>
        Task PayAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Preparing to receive the purchased currency
        /// </summary>
        /// <returns>Task</returns>
        Task PrepareToReceiveAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems swap for currency being purchased
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task RedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Redeems swap for party
        /// </summary>
        /// <returns>Task</returns>
        Task RedeemForPartyAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Refund swap for currency begin sold
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task RefundAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Start to wait redeem for swap
        /// </summary>
        /// <param name="swap">Swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task StartWaitForRedeemAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Start to wait for redeem for swap for currency being purchased in case when acceptor doesn't have funds to redeem for himself
        /// </summary>
        /// <returns>Task</returns>
        Task StartWaitForRedeemBySomeoneAsync(
            ClientSwap swap,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handle party payment tx
        /// </summary>
        /// <param name="swap">Local swap</param>
        /// <param name="clientSwap">Received swap</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task</returns>
        Task HandlePartyPaymentAsync(
            ClientSwap swap,
            ClientSwap clientSwap,
            CancellationToken cancellationToken = default);
    }
}