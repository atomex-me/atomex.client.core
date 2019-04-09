using System;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Serilog;

namespace Atomix.Blockchain.Tezos
{
    public class TezosRedeemControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }
        public string From { get; set; }
        public byte[] Secret { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                if (DateTime.UtcNow >= RefundTimeUtc)
                {
                    CancelHandler?.Invoke(this);
                    return true;
                }

                var contractAddress = Currencies.Xtz.SwapContractAddress;

                var api = (ITezosBlockchainApi)Currencies.Xtz.BlockchainApi;

                for (var page = 0; ; page++)
                {
                    var txs = (await api
                        .GetTransactionsAsync(contractAddress, page)
                        .ConfigureAwait(false))
                        .Cast<TezosTransaction>()
                        .ToList();

                    if (txs.Count == 0)
                        break;

                    foreach (var tx in txs)
                    {
                        if (tx.To.ToLowerInvariant().Equals(contractAddress.ToLowerInvariant()) &&
                            tx.IsSwapRedeem(SwapState.SecretHash))
                        {
                            // redeem!
                            Secret = tx.GetSecret();

                            Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(Secret));

                            CompleteHandler?.Invoke(this);
                            return true;
                        }

                        var blockTimeUtc = tx.BlockInfo.BlockTime.ToUniversalTime();
                        var orderTimeUtc = SwapState.Order.TimeStamp;

                        if (blockTimeUtc < orderTimeUtc)
                            return false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos redeem control task error");
            }

            return false;
        }
    }
}