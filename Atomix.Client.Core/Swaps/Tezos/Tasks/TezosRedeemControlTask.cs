using System;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Tezos;
using Serilog;

namespace Atomix.Swaps.Tezos.Tasks
{
    public class TezosRedeemControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }
        public byte[] Secret { get; private set; }

        private Atomix.Tezos Xtz => (Atomix.Tezos)Currency;

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

                        var blockTimeUtc = tx.BlockInfo.BlockTime.ToUniversalTime();
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