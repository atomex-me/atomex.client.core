using System;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.Web3;
using Serilog;

namespace Atomix.Blockchain.Ethereum
{
    public class EthereumSwapInitiatedControlTask : BlockchainTask
    {
        public const int DefaultMaxAttemptsCount = 30;

        public int MaxAttemptsCount { get; set; } = DefaultMaxAttemptsCount;
        public int AttemptsCount { get; private set; }

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                AttemptsCount++;
                if (AttemptsCount == MaxAttemptsCount)
                {
                    CancelHandler?.Invoke(this);
                    return true;
                }

                var order = Swap.Order;
                var requiredAmountInEth = AmountHelper.QtyToAmount(order.Side, order.LastQty, order.LastPrice);
                var requiredAmountInWei = Atomix.Ethereum.EthToWei(requiredAmountInEth);

                var wsUri = Web3BlockchainApi.WsUriByChain(Currencies.Eth.Chain);
                var web3 = new Web3(new WebSocketClient(wsUri));

                var contractAdderss = Currencies.Eth.SwapContractAddress;

                var eventHandler = web3.Eth.GetEvent<InitiatedEventDTO>(contractAdderss);

                var filterId = await eventHandler
                    .CreateFilterAsync(
                        Swap.SecretHash,
                        Swap.Order.ToWallet.Address)
                    .ConfigureAwait(false);

                var events = await eventHandler
                    //.GetFilterChanges(filterId)
                    .GetAllChanges(filterId)
                    .ConfigureAwait(false);

                if (events.Count == 0)
                    return false;
                
                foreach (var @event in events)
                {
                    if (@event.Event.Value >= requiredAmountInWei)
                    {
                        CompleteHandler?.Invoke(this);
                        return true;
                    }

                    Log.Debug(
                        "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                        requiredAmountInWei,
                        @event.Event.Value);
                }                  
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum swap initiated control task error");
            }

            return false;
        }
    }
}