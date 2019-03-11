namespace Atomix.Swaps.Abstract
{
    public interface ISwapClient
    {
        void SendSwapDataAsync(SwapData swapData);
    }
}