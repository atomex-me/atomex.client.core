using System.Threading.Tasks;

namespace Atomix.Swaps.Abstract
{
    public delegate void OnSwapUpdatedDelegate(object sender, SwapEventArgs swapArgs);

    public interface ISwap
    {
        OnSwapUpdatedDelegate InitiatorPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate CounterPartyPaymentConfirmed { get; set; }
        OnSwapUpdatedDelegate CounterPartyPaymentSpent { get; set; }

        Task InitiateSwapAsync();
        Task AcceptSwapAsync();
        Task RestoreSwapAsync();
        Task HandleSwapData(SwapData swapData);
        Task RedeemAsync();
        Task BroadcastPaymentAsync();
    }
}