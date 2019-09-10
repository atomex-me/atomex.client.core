using Atomix.Core.Entities;

namespace Atomix.Swaps.Abstract
{
    public interface ISwapClient
    {
        void SwapInitiateAsync(ClientSwap swap);
        void SwapAcceptAsync(ClientSwap swap);
        void SwapPaymentAsync(ClientSwap swap);
    }
}