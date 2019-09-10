using System;
using System.Linq;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Common;
using Serilog;

namespace Atomex.Swaps.Tezos.Tasks
{
    public class TezosSwapInitiatedControlTask : BlockchainTask 
    {
        private const int DefaultMaxAttemptsCount = 60;

        private int MaxAttemptsCount { get; } = DefaultMaxAttemptsCount;
        private int AttemptsCount { get; set; }
        public long RefundTimestamp { get; set; }

        private Atomex.Tezos Xtz => (Atomex.Tezos)Currency;

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                Log.Debug("Tezos: check initiated event");

                AttemptsCount++;
                if (AttemptsCount == MaxAttemptsCount)
                {
                    Log.Warning("Tezos: maximum number of attempts to check initiated event reached");

                    CancelHandler?.Invoke(this);
                    return true;
                }

                var side = Swap.Symbol
                    .OrderSideForBuyCurrency(Swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInTz = AmountHelper.QtyToAmount(side, Swap.Qty, Swap.Price);
                var requiredAmountInMtz = requiredAmountInTz.ToMicroTez();
                var requiredRewardForRedeemInMtz = Swap.RewardForRedeem.ToMicroTez();

                var contractAddress = Xtz.SwapContractAddress;

                var api = (ITezosBlockchainApi) Xtz.BlockchainApi;

                var detectedAmountInMtz = 0m;
                var detectedRedeemFeeAmountInMtz = 0m;

                for (var page = 0;; page++)
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
                        if (tx.IsConfirmed() && tx.To == contractAddress)
                        {
                            var detectedPayment = false;

                            if (tx.IsSwapInit(RefundTimestamp, Swap.SecretHash, Swap.ToAddress))
                            {
                                // init payment to secret hash!
                                detectedPayment = true;
                                detectedAmountInMtz += tx.Amount;
                                detectedRedeemFeeAmountInMtz = tx.GetRedeemFee();
                            }
                            else if (tx.IsSwapAdd(Swap.SecretHash))
                            {
                                detectedPayment = true;
                                detectedAmountInMtz += tx.Amount;
                            }

                            if (detectedPayment && detectedAmountInMtz >= requiredAmountInMtz)
                            {
                                if (Swap.IsAcceptor && detectedRedeemFeeAmountInMtz != requiredRewardForRedeemInMtz)
                                {
                                    CancelHandler?.Invoke(this);
                                    return true;
                                }

                                CompleteHandler?.Invoke(this);
                                return true;
                            }
                        }

                        var blockTimeUtc = tx.BlockInfo.BlockTime.ToUniversalTime();
                        var swapTimeUtc = Swap.TimeStamp.ToUniversalTime();

                        if (blockTimeUtc < swapTimeUtc)
                            return false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos swap initiated control task error");
            }

            return false;
        }
    }
}