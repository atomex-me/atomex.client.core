using System;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.Web3;
using Serilog;

namespace Atomix.Blockchain.Ethereum
{
    public class EthereumRedeemControlTask : BlockchainTask
    {
        public DateTime RefundTime { get; set; }
        public string From { get; set; }
        public byte[] Secret { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                if (DateTime.Now >= RefundTime)
                {
                    CancelHandler?.Invoke(this);
                    return true;
                }

                var wsUri = Web3BlockchainApi.WsUriByChain(Currencies.Eth.Chain);
                var web3 = new Web3(new WebSocketClient(wsUri));

                var redeemEventHandler = web3.Eth
                    .GetEvent<RedeemedEventDTO>(Currencies.Eth.SwapContractAddress);

                var filter = redeemEventHandler
                    .CreateFilterInput<byte[]>(Swap.SecretHash);

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

            return false;
        }
    }
}