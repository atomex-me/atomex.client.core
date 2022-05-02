using System;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;

namespace Atomex.Swaps.BitcoinBased.Helpers
{
    public class BitcoinBasedSwapInitiatedHelper
    {
        public static async Task<Result<IBlockchainTransaction_OLD>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            Side side,
            string toAddress,
            string refundAddress,
            long refundTimeStamp,
            string redeemScriptBase64 = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("BitcoinBased: try to find payment tx");

                var bitcoinBased = (BitcoinBasedConfig)currency;

                var requiredAmount = AmountHelper.QtyToSellAmount(side, swap.Qty, swap.Price, bitcoinBased.DigitsMultiplier);
                var requiredAmountInSatoshi = bitcoinBased.CoinToSatoshi(requiredAmount);

                var redeemScript = refundAddress == null && redeemScriptBase64 != null
                    ? new Script(Convert.FromBase64String(redeemScriptBase64))
                    : BitcoinBasedSwapTemplate
                        .GenerateHtlcP2PkhSwapPayment(
                            aliceRefundAddress: refundAddress,
                            bobAddress: toAddress,
                            lockTimeStamp: refundTimeStamp,
                            secretHash: swap.SecretHash,
                            secretSize: CurrencySwap.DefaultSecretSize,
                            expectedNetwork: bitcoinBased.Network);

                var redeemScriptAddress = redeemScript
                    .PaymentScript
                    .GetDestinationAddress(bitcoinBased.Network)
                    .ToString();

                var api = bitcoinBased.BlockchainApi as BitcoinBasedBlockchainApi_OLD;

                var outputsResult = await api
                    .GetOutputsAsync(redeemScriptAddress, null, cancellationToken)
                    .ConfigureAwait(false);

                if (outputsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting outputs for {redeemScriptAddress} address");

                if (outputsResult.HasError)
                    return outputsResult.Error;

                foreach (var output in outputsResult.Value)
                {
                    var o = output as BitcoinBasedTxOutput;

                    var outputScriptHex = o.Coin.TxOut.ScriptPubKey.ToHex();

                    if (redeemScript.PaymentScript.ToHex() != outputScriptHex)
                        continue;

                    if (o.Value < requiredAmountInSatoshi)
                        continue;

                    var txResult = await api
                        .GetTransactionAsync(o.TxId, cancellationToken)
                        .ConfigureAwait(false);

                    if (txResult == null)
                        return new Error(Errors.RequestError, $"Connection error while getting tx {o.TxId}");

                    if (txResult.HasError)
                        return txResult.Error;

                    if (txResult.Value == null)
                        continue;

                    return txResult.Value as BitcoinBasedTransaction;
                }

                return new Result<IBlockchainTransaction_OLD>((IBitcoinBasedTransaction_OLD)null);
            }
            catch (Exception e)
            {
                Log.Error(e, "BitcoinBased swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            CurrencyConfig currency,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("BitcoinBased: check initiated event");

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var txResult = await TryToFindPaymentAsync(
                        swap: swap,
                        currency: currency,
                        side: side,
                        toAddress: swap.ToAddress,
                        refundAddress: swap.PartyRefundAddress,
                        refundTimeStamp: refundTimeStamp,
                        redeemScriptBase64: swap.PartyRedeemScript,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (txResult == null)
                    return new Error(Errors.RequestError, $"Connection error while getting payment tx");

                if (txResult.HasError)
                    return txResult.Error;

                if (txResult.Value == null)
                    return false;

                return txResult.Value.IsConfirmed;
            }
            catch (Exception e)
            {
                Log.Error(e, "BitcoinBased swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }
        }

        public static Task StartSwapInitiatedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler = null,
            Func<Swap, CancellationToken, Task> canceledHandler = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (swap.IsCanceled)
                        {
                            await canceledHandler.Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        var isInitiatedResult = await IsInitiatedAsync(
                                swap: swap,
                                currency: currency,
                                refundTimeStamp: refundTimeStamp,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (isInitiatedResult.HasError && isInitiatedResult.Error.Code != Errors.RequestError)
                        {
                            await canceledHandler.Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }
                        else if (!isInitiatedResult.HasError && isInitiatedResult.Value)
                        {
                            await initiatedHandler.Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        await Task.Delay(interval, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapInitiatedControlAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapInitiatedControlAsync error.");
                }

            }, cancellationToken);
        }
    }
}