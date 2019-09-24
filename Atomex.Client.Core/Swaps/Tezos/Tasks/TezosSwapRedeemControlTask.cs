using System;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Serilog;

namespace Atomex.Swaps.Tezos.Tasks
{
    public class TezosSwapRedeemControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }
        public byte[] Secret { get; private set; }
        public bool CancelOnlyWhenRefundTimeReached { get; set; } = true;

        private Atomex.Tezos Xtz => (Atomex.Tezos)Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Tezos: check redeem event");

                var contractAddress = Xtz.SwapContractAddress;

                var api = (ITezosBlockchainApi)Xtz.BlockchainApi;

                for (var page = 0; ; page++)
                {
                    var txs = (await api
                        .GetTransactionsAsync(contractAddress, page)
                        .ConfigureAwait(false))
                        .Cast<TezosTransaction>()
                        .ToList();

                    if (txs.Count == 0)
                        break;

                    var swapTimeReached = false;

                    foreach (var tx in txs)
                    {
                        if (tx.To == contractAddress && tx.IsSwapRedeem(Swap.SecretHash))
                        {
                            // redeem!
                            Secret = tx.GetSecret();

                            Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(Secret));

                            CompleteHandler?.Invoke(this);
                            return true;
                        }

                        if (tx.BlockInfo?.BlockTime == null)
                            continue;

                        var blockTimeUtc = tx.BlockInfo.BlockTime.Value.ToUniversalTime();
                        var swapTimeUtc = Swap.TimeStamp.ToUniversalTime();

                        if (blockTimeUtc < swapTimeUtc)
                        {
                            swapTimeReached = true;
                            break;
                        }
                    }

                    if (swapTimeReached)
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos redeem control task error");
            }

            if (!CancelOnlyWhenRefundTimeReached)
                CancelHandler?.Invoke(this);

            if (DateTime.UtcNow >= RefundTimeUtc)
            {
                Log.Debug("Time for refund reached");

                CancelHandler?.Invoke(this);
                return true;
            }

            return false;
        }
    }
}