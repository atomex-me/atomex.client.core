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

                var txId = await Currency.BlockchainApi
                    .BroadcastAsync(refundTx)
                    .ConfigureAwait(false);

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
