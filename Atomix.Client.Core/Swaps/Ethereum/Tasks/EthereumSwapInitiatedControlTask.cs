using System;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Ethereum;
using Atomix.Common;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.Web3;
using Serilog;

namespace Atomix.Swaps.Ethereum.Tasks
{
    public class EthereumSwapInitiatedControlTask : BlockchainTask
    {
        private const int DefaultMaxAttemptsCount = 60;

        private int MaxAttemptsCount { get;  } = DefaultMaxAttemptsCount;
        private int AttemptsCount { get; set; }
        public long RefundTimestamp { get; set; }

        private bool Initiated { get; set; }

        private Atomix.Ethereum Eth => (Atomix.Ethereum)Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Ethereum: check initiated event");

                AttemptsCount++;
                if (AttemptsCount == MaxAttemptsCount)
                {
                    Log.Warning("Ethereum: maximum number of attempts to check initiated event reached");

                    CancelHandler?.Invoke(this);
                    return true;
                }

                var side = Swap.Symbol
                    .OrderSideForBuyCurrency(Swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInEth = AmountHelper.QtyToAmount(side, Swap.Qty, Swap.Price);
                var requiredAmountInWei = Atomix.Ethereum.EthToWei(requiredAmountInEth);
                var requiredRewardForRedeemInWei = Atomix.Ethereum.EthToWei(Swap.RewardForRedeem);

                var wsUri = Web3BlockchainApi.WsUriByChain(Eth.Chain);
                var web3 = new Web3(new WebSocketClient(wsUri));

                var contractAddress = Eth.SwapContractAddress;

                if (!Initiated)
                {
                    var eventHandlerInitiated = web3.Eth.GetEvent<InitiatedEventDTO>(contractAddress);

                    var filterIdInitiated = await eventHandlerInitiated
                        .CreateFilterAsync(
                            Swap.SecretHash,
                            Swap.ToAddress)
                        .ConfigureAwait(false);

                    var eventInitiated = await eventHandlerInitiated
                        //.GetFilterChanges(filterId)
                        .GetAllChanges(filterIdInitiated)
                        .ConfigureAwait(false);

                    if (eventInitiated.Count == 0)
                        return false;

                    Initiated = true;

                    if (eventInitiated[0].Event.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                    {
                        if (Swap.IsAcceptor)
                        {
                            if (eventInitiated[0].Event.RedeemFee != requiredRewardForRedeemInWei)
                            {
                                Log.Debug(
                                    "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    requiredRewardForRedeemInWei,
                                    (long)eventInitiated[0].Event.RedeemFee);

                                CancelHandler?.Invoke(this);
                                return true;
                            }

                            if (eventInitiated[0].Event.RefundTimestamp != RefundTimestamp)
                            {
                                Log.Debug(
                                    "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    RefundTimestamp,
                                    eventInitiated[0].Event.RefundTimestamp);

                                CancelHandler?.Invoke(this);
                                return true;
                            }
                        }

                        CompleteHandler?.Invoke(this);
                        return true;
                    }

                    Log.Debug(
                        "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                        requiredAmountInWei - requiredRewardForRedeemInWei,
                        (long)eventInitiated[0].Event.Value);
                }

                if (Initiated)
                {
                    var eventHandlerAdded = web3.Eth.GetEvent<AddedEventDTO>(contractAddress);

                    var filterIdAdded = await eventHandlerAdded
                        .CreateFilterAsync<byte[]>(Swap.SecretHash)
                        .ConfigureAwait(false);

                    var eventsAdded = await eventHandlerAdded
                        //.GetFilterChanges(filterId)
                        .GetAllChanges(filterIdAdded)
                        .ConfigureAwait(false);

                    if (eventsAdded.Count == 0)
                        return false;

                    foreach (var @event in eventsAdded)
                    {
                        if (@event.Event.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                        {
                            CompleteHandler?.Invoke(this);
                            return true;
                        }

                        Log.Debug(
                            "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                            requiredAmountInWei - requiredRewardForRedeemInWei,
                            (long)@event.Event.Value);
                    }
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