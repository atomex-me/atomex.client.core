using Atomex.Core.Entities;

namespace Atomex.Swaps.Abstract
{
    public interface ISwapClient
    {
        void SwapInitiateAsync(ClientSwap swap);
        void SwapAcceptAsync(ClientSwap swap);
        void SwapPaymentAsync(ClientSwap swap);
    }
}