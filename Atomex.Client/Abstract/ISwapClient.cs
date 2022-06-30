namespace Atomex.Client.Abstract
{
    public interface ISwapClient
    {
        void SwapInitiateAsync(
            long id,
            byte[] secretHash,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress,
            ulong lockTime);

        void SwapAcceptAsync(
            long id,
            string symbol,
            string toAddress,
            decimal rewardForRedeem,
            string refundAddress,
            ulong lockTime);

        void SwapStatusAsync(
            string requestId,
            long swapId);
    }
}