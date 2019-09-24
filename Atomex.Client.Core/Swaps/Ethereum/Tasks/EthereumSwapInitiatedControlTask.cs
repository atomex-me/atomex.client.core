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
    public class EthereumSwapInitiatedControlTask : BlockchainTask
    {
        private const int DefaultMaxAttemptsCount = 60;

        private int MaxAttemptsCount { get;  } = DefaultMaxAttemptsCount;
        private int AttemptsCount { get; set; }
        public long RefundTimestamp { get; set; }

        private bool Initiated { get; set; }

        private Atomex.Ethereum Eth => (Atomex.Ethereum)Currency;

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
                var requiredAmountInWei = Atomex.Ethereum.EthToWei(requiredAmountInEth);
                var requiredRewardForRedeemInWei = Atomex.Ethereum.EthToWei(Swap.RewardForRedeem);

                var api = new EtherScanApi(Eth, Eth.Chain);

                if (!Initiated)
                {
                    var events = (await api.GetContractEventsAsync(
                            address: Eth.SwapContractAddress,
                            fromBlock: Eth.SwapContractBlockNumber,
                            toBlock: ulong.MaxValue,
                            topic0: EventSignatureExtractor.GetSignatureHash<InitiatedEventDTO>(),
                            topic1: "0x" + Swap.SecretHash.ToHexString(),
                            topic2: "0x000000000000000000000000" + Swap.ToAddress.Substring(2))
                        .ConfigureAwait(false))
                        ?.ToList() ?? new List<EtherScanApi.ContractEvent>();

                    if (!events.Any())
                        return false;

                    var initiatedEvent = events.First().ParseInitiatedEvent();

                    Initiated = true;

                    if (initiatedEvent.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                    {
                        if (Swap.IsAcceptor)
                        {
                            if (initiatedEvent.RedeemFee != requiredRewardForRedeemInWei)
                            {
                                Log.Debug(
                                    "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    requiredRewardForRedeemInWei,
                                    (long)initiatedEvent.RedeemFee);

                                CancelHandler?.Invoke(this);
                                return true;
                            }

                            if (initiatedEvent.RefundTimestamp != RefundTimestamp)
                            {
                                Log.Debug(
                                    "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                                    RefundTimestamp,
                                    (long)initiatedEvent.RefundTimestamp);

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
                        (long)initiatedEvent.Value);
                }

                if (Initiated)
                {
                    var events = (await api.GetContractEventsAsync(
                            address: Eth.SwapContractAddress,
                            fromBlock: Eth.SwapContractBlockNumber,
                            toBlock: ulong.MaxValue,
                            topic0: EventSignatureExtractor.GetSignatureHash<AddedEventDTO>(),
                            topic1: "0x" + Swap.SecretHash.ToHexString())
                        .ConfigureAwait(false))
                        ?.ToList() ?? new List<EtherScanApi.ContractEvent>();

                    if (!events.Any())
                        return false;

                    foreach (var @event in events.Select(e => e.ParseAddedEvent()))
                    {
                        if (@event.Value >= requiredAmountInWei - requiredRewardForRedeemInWei)
                        {
                            CompleteHandler?.Invoke(this);
                            return true;
                        }

                        Log.Debug(
                            "Eth value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                            requiredAmountInWei - requiredRewardForRedeemInWei,
                            (long)@event.Value);
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