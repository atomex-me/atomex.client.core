using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos;
using Atomex.Blockchain.Tezos.Tzkt;
using Atomex.Blockchain.Tezos.Tzkt.Swaps.V1;
using Atomex.Common;
using Atomex.Core;
using Atomex.Swaps.Abstract;

namespace Atomex.Swaps.Tezos.Helpers
{
    public static class TezosSwapInitiatedHelper
    {
        public static async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            var tezos = currency as TezosConfig;

            var lockTimeInSeconds = swap.IsInitiator
                ? CurrencySwap.DefaultInitiatorLockTimeInSeconds
                : CurrencySwap.DefaultAcceptorLockTimeInSeconds;

            var rewardForRedeemInMtz = swap.IsInitiator
                ? swap.PartyRewardForRedeem.ToMicroTez()
                : 0;

            var api = new TzktApi(tezos.GetTzktSettings());

            var (ops, error) = await api
                .FindLocksAsync(
                    secretHash: swap.SecretHash.ToHexString(),
                    contractAddress: tezos.SwapContractAddress,
                    address: swap.PartyAddress,
                    timeStamp: (ulong)swap.TimeStamp.ToUnixTimeSeconds(),
                    lockTime: (ulong)lockTimeInSeconds,
                    payoff: rewardForRedeemInMtz,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (ops == null)
                return new Error(Errors.RequestError, "Can't get Tezos swap contract transactions");

            foreach (var op in ops)
                if (op.Status != TransactionStatus.Failed)
                    return op;

            return new Result<ITransaction> { Value = null };
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            CurrencyConfig currency,
            long refundTimeStamp,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Tezos: check initiated event");

                var tezos = (TezosConfig)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var requiredAmountInMtz = AmountHelper
                    .QtyToSellAmount(side, swap.Qty, swap.Price, tezos.Precision)
                    .ToMicroTez();

                var requiredRewardForRedeemInMtz = swap.RewardForRedeem.ToMicroTez();

                var secretHash = swap.SecretHash.ToHexString();
                var contractAddress = tezos.SwapContractAddress;
                var timeStamp = swap.TimeStamp.ToUnixTimeSeconds();
                var lockTime = refundTimeStamp - timeStamp;

                var api = new TzktApi(tezos.GetTzktSettings());

                var (locks, error) = await api
                    .FindLocksAsync(
                        secretHash: secretHash,
                        contractAddress: contractAddress,
                        address: swap.ToAddress,
                        timeStamp: (ulong)timeStamp,
                        lockTime: (ulong)lockTime,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    Log.Error("Error while get locks transactions from contract {@contract}. Code: {@code}. Message: {@mes}",
                        contractAddress,
                        error.Value.Code,
                        error.Value.Message);

                    return error;
                }

                if (locks == null || !locks.Any())
                    return false;

                var (addLocks, addLocksError) = await TzktSwapHelper
                    .FindAdditionalLocksAsync(
                        api: api,
                        secretHash: secretHash,
                        contractAddress: contractAddress,
                        timeStamp: (ulong)timeStamp,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (addLocksError != null)
                {
                    Log.Error("Error while get additional locks transactions from contract {@contract}. Code: {@code}. Message: {@mes}",
                        contractAddress,
                        addLocksError.Value.Code,
                        addLocksError.Value.Message);

                    return addLocksError;
                }

                var operations = (addLocks != null && addLocks.Any())
                    ? locks.Concat(addLocks)
                    : locks;

                var detectedAmountInMtz = 0m;
                var detectedRedeemFeeAmountInMtz = 0m;

                foreach (var op in operations)
                {
                    if (!op.IsConfirmed)
                        continue;

                    if (op.TryFindInitiate(
                        contractAddress: contractAddress,
                        secretHash: swap.SecretHash.ToHexString(),
                        refundTime: refundTimeStamp,
                        participant: swap.ToAddress,
                        payoff: requiredRewardForRedeemInMtz,
                        initiateTx: out var initiateTx))
                    {
                        detectedAmountInMtz += initiateTx.Amount;
                    }
                    else if (op.TryFindAdd(
                        contractAddress: contractAddress,
                        secretHash: swap.SecretHash.ToHexString(),
                        out var addTx))
                    {
                        detectedAmountInMtz += addTx.Amount;
                    }

                    if (detectedAmountInMtz >= requiredAmountInMtz)
                    {
                        if (swap.IsAcceptor && detectedRedeemFeeAmountInMtz != requiredRewardForRedeemInMtz)
                        {
                            Log.Debug(
                                "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                                requiredRewardForRedeemInMtz,
                                detectedRedeemFeeAmountInMtz);

                            return new Error(
                                code: Errors.InvalidRewardForRedeem,
                                message: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInMtz}, actual is {detectedRedeemFeeAmountInMtz}");
                        }

                        return true;
                    }

                    var blockTimeUtc = op.BlockTime.Value.ToUniversalTime();
                    var swapTimeUtc = swap.TimeStamp.ToUniversalTime();

                    if (blockTimeUtc < swapTimeUtc)
                        return false;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Tezos swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static async Task StartSwapInitiatedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            long refundTimeStamp,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} started", currency.Name, swap.Id);

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
        }
    }
}