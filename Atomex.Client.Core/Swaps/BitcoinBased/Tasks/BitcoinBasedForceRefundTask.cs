using System;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Serilog;

namespace Atomex.Swaps.BitcoinBased.Tasks
{
    public class BitcoinBasedForceRefundTask : BlockchainTask
    {
        public int MaxAttempts { get; set; } = int.MaxValue;
        public string RefundTxId { get; private set; }

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                MaxAttempts--;

                if (MaxAttempts <= 0)
                {
                    CancelHandler(this);
                    return true;
                }

                var refundTx = (IBitcoinBasedTransaction)Swap.RefundTx;

                var asyncResult = await Currency.BlockchainApi
                    .BroadcastAsync(refundTx)
                    .ConfigureAwait(false);

                if (asyncResult.HasError)
                {
                    Log.Error("Error while broadcast refund tx with code {@code} and description {@description}",
                        asyncResult.Error.Code, 
                        asyncResult.Error.Description);
                    return false;
                }

                var txId = asyncResult.Value;

                if (txId != null)
                {
                    RefundTxId = txId;
                    CompleteHandler(this);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Bitcoin based force refund task error");
            }

            return false;
        }
    }
}
