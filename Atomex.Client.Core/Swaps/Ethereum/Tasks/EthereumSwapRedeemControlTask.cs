using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Serilog;

namespace Atomex.Swaps.Ethereum.Tasks
{
    public class EthereumSwapRedeemControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }
        public byte[] Secret { get; private set; }
        public bool CancelOnlyWhenRefundTimeReached { get; set; } = true;

        private Atomex.Ethereum Eth => (Atomex.Ethereum) Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Ethereum: check redeem event");

                var api = new EtherScanApi(Eth, Eth.Chain);

                var events = (await api.GetContractEventsAsync(
                        address: Eth.SwapContractAddress,
                        fromBlock: Eth.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<RedeemedEventDTO>(),
                        topic1: "0x" + Swap.SecretHash.ToHexString())
                    .ConfigureAwait(false))
                    ?.ToList() ?? new List<EtherScanApi.ContractEvent>();

                if (events.Count > 0)
                {
                    Secret = events.First().ParseRedeemedEvent().Secret;

                    Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(Secret));

                    CompleteHandler?.Invoke(this);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum redeem control task error");
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