using System;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Serilog;

namespace Atomex.Swaps.Ethereum.Tasks
{
    public class EthereumRedeemControlTask : BlockchainTask
    {
        public DateTime RefundTimeUtc { get; set; }
        public byte[] Secret { get; private set; }

        private Atomex.Ethereum Eth => (Atomex.Ethereum) Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Ethereum: check redeem event");

                var wsUri = Web3BlockchainApi.WsUriByChain(Eth.Chain);
                var web3 = new Web3(new WebSocketClient(wsUri));

                var redeemEventHandler = web3.Eth
                    .GetEvent<RedeemedEventDTO>(Eth.SwapContractAddress);

                var filter = redeemEventHandler
                    .CreateFilterInput<byte[]>(
                        Swap.SecretHash,
                        new BlockParameter(Eth.SwapContractBlockNumber));

                var events = await redeemEventHandler
                    .GetAllChanges(filter)
                    .ConfigureAwait(false);

                if (events.Count > 0)
                {
                    Secret = events.First().Event.Secret;

                    Log.Debug("Redeem event received with secret {@secret}", Convert.ToBase64String(Secret));

                    CompleteHandler?.Invoke(this);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum redeem control task error");
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