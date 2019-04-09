using System;
using System.Linq;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Atomix.Swaps;
using Serilog;

namespace Atomix.Blockchain.Tezos
{
    public class TezosSwapInitiatedControlTask : BlockchainTask
    {
        public const int DefaultMaxAttemptsCount = 60;

        public int MaxAttemptsCount { get; } = DefaultMaxAttemptsCount;
        public int AttemptsCount { get; private set; }
        public long RefundTime { get; set; }

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

                var order = SwapState.Order;

                var side = order.Symbol
                    .OrderSideForBuyCurrency(order.PurchasedCurrency())
                    .Opposite();

                var requiredAmountInTz = AmountHelper.QtyToAmount(side, order.LastQty, order.LastPrice);
                var requiredAmountInMtz = requiredAmountInTz.ToMicroTez();

                var contractAddress = Currencies.Xtz.SwapContractAddress;

                var api = (ITezosBlockchainApi) Currencies.Xtz.BlockchainApi;

                var detectedAmountInMtz = 0m;

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
                        if (tx.IsConfirmed() &&
                            tx.To.ToLowerInvariant().Equals(contractAddress.ToLowerInvariant()) &&
                            tx.IsSwapPayment(RefundTime, SwapState.SecretHash, SwapState.Order.ToWallet.Address))
                        {
                            // payment to secret hash!
                            detectedAmountInMtz += tx.Amount;

                            if (detectedAmountInMtz >= requiredAmountInMtz)
                            {
                                CompleteHandler?.Invoke(this);
                                return true;
                            }
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
                Log.Error(e, "Tezos swap initiated control task error");
            }

            return false;
        }
    }
}