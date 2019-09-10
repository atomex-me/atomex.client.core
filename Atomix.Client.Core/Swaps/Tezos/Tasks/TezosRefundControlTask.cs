using System;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Tezos;
using Serilog;

namespace Atomix.Swaps.Tezos.Tasks
{
    public class TezosRefundControlTask : BlockchainTask
    {
        private Atomix.Tezos Xtz => (Atomix.Tezos)Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Tezos: check refund event");

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
                        if (tx.To == contractAddress && tx.IsSwapRefund(Swap.SecretHash))
                        {
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
                Log.Error(e, "Tezos refund control task error");
            }

            CancelHandler?.Invoke(this);
            return true;
        }
    }
}