using System;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;

namespace Atomex.Swaps.BitcoinBased.Helpers
{
    public class BitcoinBasedSwapInitiatedHelper
    {
        public static async Task<Result<ITransaction>> TryToFindPaymentAsync(
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
                    : BitcoinSwapTemplate
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

                var api = bitcoinBased.BlockchainApi as BitcoinBlockchainApi;

                var (outputs, error) = await api
                    .GetOutputsAsync(redeemScriptAddress, cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (outputs == null)
                    return new Error(Errors.RequestError, $"Connection error while getting outputs for {redeemScriptAddress} address");

                foreach (var output in outputs)
                {
                    var outputScriptHex = output.Coin.TxOut.ScriptPubKey.ToHex();

                    if (redeemScript.PaymentScript.ToHex() != outputScriptHex)
                        continue;

                    if (output.Value < requiredAmountInSatoshi)
                        continue;

                    var (tx, txError) = await api
                        .GetTransactionAsync(output.TxId, cancellationToken)
                        .ConfigureAwait(false);

                    if (txError != null)
                        return txError;

                    if (tx == null)
                        continue;

                    return tx as BitcoinTransaction;
                }

                return new Result<ITransaction> { Value = null };
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

                var (tx, error) = await TryToFindPaymentAsync(
                        swap: swap,
                        currency: currency,
                        side: side,
                        toAddress: swap.ToAddress,
                        refundAddress: swap.PartyRefundAddress,
                        refundTimeStamp: refundTimeStamp,
                        redeemScriptBase64: swap.PartyRedeemScript,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (tx == null)
                    return false;

                return tx.IsConfirmed;
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
            Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} started", currency.Name, swap.Id);

            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (swap.IsCanceled || DateTimeOffset.UtcNow >= DateTimeOffset.FromUnixTimeSeconds(refundTimeStamp))
                        {
                            await canceledHandler
                                .Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        var (isInitiated, error) = await IsInitiatedAsync(
                                swap: swap,
                                currency: currency,
                                refundTimeStamp: refundTimeStamp,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("{@currency} IsInitiatedAsync error for swap {@swap}. Code: {@code}. Description: {@desc}",
                                currency.Name,
                                swap.Id,
                                error.Value.Code,
                                error.Value.Message);
                        }
                        else if (error == null && isInitiated)
                        {
                            await initiatedHandler
                                .Invoke(swap, cancellationToken)
                                .ConfigureAwait(false);

                            break;
                        }

                        await Task.Delay(interval, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} {@message}",
                        currency.Name,
                        swap.Id,
                        cancellationToken.IsCancellationRequested ? "canceled" : "completed");
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} canceled",
                        currency.Name,
                        swap.Id);
                }
                catch (Exception e)
                {
                    Log.Error(e, "StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} error",
                        currency.Name,
                        swap.Id);
                }

            }, cancellationToken);
        }
    }
}