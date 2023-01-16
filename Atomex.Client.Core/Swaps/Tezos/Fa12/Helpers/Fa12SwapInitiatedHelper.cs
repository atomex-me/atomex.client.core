using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Blockchain.Tezos.Tzkt.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Swaps.Abstract;

namespace Atomex.Swaps.Tezos.Fa12.Helpers
{
    public static class Fa12SwapInitiatedHelper
    {
        public static async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            var fa12 = currency as Fa12Config;

            var lockTimeInSeconds = swap.IsInitiator
                ? CurrencySwap.DefaultInitiatorLockTimeInSeconds
                : CurrencySwap.DefaultAcceptorLockTimeInSeconds;

            var rewardForRedeemInTokenDigits = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToTokenDigits(fa12.DigitsMultiplier)
                : 0;

            var requiredAmountInTokens = Fa12Swap.RequiredAmountInTokens(swap, fa12);
            var requiredAmountInTokensDigits = requiredAmountInTokens.ToTokenDigits(fa12.DigitsMultiplier);

            var api = new TzktApi(fa12.GetTzktSettings());

            var (ops, error) = await api
                .FindFa12LocksAsync(
                    secretHash: swap.SecretHash.ToHexString(),
                    contractAddress: fa12.SwapContractAddress,
                    address: swap.PartyAddress,
                    timeStamp: (ulong)swap.TimeStamp.ToUnixTimeSeconds(),
                    lockTime: (ulong)lockTimeInSeconds,
                    tokenContract: fa12.TokenContractAddress,
                    totalAmount: new BigInteger(requiredAmountInTokensDigits),
                    payoffAmount: new BigInteger(rewardForRedeemInTokenDigits),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (ops == null)
                return new Error(Errors.RequestError, "Can't get Fa12 swap contract transactions");

            foreach (var op in ops)
                if (op.Status != TransactionStatus.Failed)
                    return op;

            return new Result<ITransaction> { Value = null };
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos FA12: check initiated event");

                var fa12 = (Fa12Config)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInTokenDigits = AmountHelper
                    .QtyToSellAmount(side, swap.Qty, swap.Price, fa12.DigitsMultiplier)
                    .ToTokenDigits(fa12.DigitsMultiplier);

                var requiredRewardForRedeemInTokenDigits = swap.IsAcceptor
                    ? swap.RewardForRedeem.ToTokenDigits(fa12.DigitsMultiplier)
                    : 0;

                var secretHash = swap.SecretHash.ToHexString();
                var contractAddress = fa12.SwapContractAddress;
                var timeStamp = swap.TimeStamp.ToUnixTimeSeconds();
                var lockTime = refundTimeStamp - timeStamp;

                var api = new TzktApi(tezos.GetTzktSettings());

                var (ops, error) = await api
                    .FindFa12LocksAsync(
                        secretHash: secretHash,
                        contractAddress: contractAddress,
                        address: swap.ToAddress,
                        timeStamp: (ulong)timeStamp,
                        lockTime: (ulong)lockTime,
                        tokenContract: fa12.TokenContractAddress,
                        totalAmount: new BigInteger(requiredAmountInTokenDigits),
                        payoffAmount: new BigInteger(requiredRewardForRedeemInTokenDigits),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("Error while get transactions from contract {@contract}. Code: {@code}. Description: {@desc}",
                        contractAddress,
                        error.Value.Code,
                        error.Value.Message);

                    return error;
                }

                return ops != null && ops.Any(op => op.IsConfirmed);
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos token swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }
        }

        public static Task StartSwapInitiatedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            TezosConfig tezos,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} started",
                currency.Name,
                swap.Id);

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
                                tezos: tezos,
                                refundTimeStamp: refundTimeStamp,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (error != null)
                        {
                            Log.Error("{@currency} IsInitiatedAsync error for swap {@swap}. Code: {@code}. Message: {@desc}",
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