using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Hex.HexTypes;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.Erc20.Dto;
using Atomex.Blockchain.Ethereum.Erc20.Dto.Swaps.V1;
using Atomex.Blockchain.Ethereum.EtherScan;
using Atomex.Common;
using Atomex.Core;
using Atomex.EthereumTokens;

namespace Atomex.Swaps.Ethereum.Erc20.Helpers
{
    public static class Erc20SwapInitiatedHelper
    {
        private const int BlocksAhead = 10000; // >24h with block time 10-30 seconds

        public static async Task<Result<ITransaction>> TryToFindPaymentAsync(
            Swap swap,
            Erc20Config erc20Config,
            CancellationToken cancellationToken = default)
        {
            var api = erc20Config.GetEtherScanApi();

            var (blockNo, blockError) = await api
                .GetBlockNumberAsync(
                    timeStamp: swap.TimeStamp.ToUniversalTime(),
                    blockClosest: ClosestBlock.Before,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (blockError != null)
                return blockError;

            var (txs, error) = await api
                .GetTransactionsAsync(
                    address: erc20Config.SwapContractAddress,
                    fromBlock: blockNo,
                    toBlock: blockNo + BlocksAhead,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null) 
                return error;

            foreach (var tx in txs.Cast<EthereumTransaction>())
            {
                if (tx.Amount != 0 ||
                   !tx.From.Equals(swap.FromAddress, StringComparison.OrdinalIgnoreCase) ||
                   !tx.To.Equals(erc20Config.SwapContractAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!tx.Data.Contains(swap.SecretHash.ToHexString()) ||
                    !tx.Data.Contains(swap.PartyAddress) ||
                    !tx.Data.Contains(erc20Config.ERC20ContractAddress))
                    continue;

                if (tx.Status == TransactionStatus.Failed)
                    continue; // skip failed transactions

                return tx;
            }

            return new Result<ITransaction> { Value = null };
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            Erc20Config erc20,
            long lockTimeInSec,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum ERC20: check initiated event");

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var refundTimeStamp = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSec)).ToUnixTimeSeconds();
                var requiredAmount = AmountHelper.QtyToSellAmount(side, swap.Qty, swap.Price, erc20.Precision);
                var requiredAmountInDecimals = erc20.TokensToTokenDigits(requiredAmount);
                var requiredRewardForRedeemInDecimals = swap.IsAcceptor
                    ? erc20.TokensToTokenDigits(swap.RewardForRedeem)
                    : 0;

                var api = erc20.GetEtherScanApi();

                var (events, initiateError) = await api
                    .GetContractEventsAsync(
                        address: erc20.SwapContractAddress,
                        fromBlock: erc20.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<Erc20InitiatedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        topic2: "0x000000000000000000000000" + erc20.ERC20ContractAddress[2..], //??
                        topic3: "0x000000000000000000000000" + swap.ToAddress[2..],
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (initiateError != null)
                    return initiateError;

                if (events == null || !events.Any())
                    return false;

                var contractInitEvent = events.Last();

                var initiatedEvent = contractInitEvent.ParseErc20InitiatedEvent();

                if (initiatedEvent.RefundTimestamp < refundTimeStamp)
                {
                    Log.Debug(
                        "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                        refundTimeStamp,
                        (long)initiatedEvent.RefundTimestamp);

                    return new Error(
                        code: Errors.InvalidRefundLockTime,
                        message: $"Invalid refund time in initiated event. Expected value is {refundTimeStamp}, actual is {(long)initiatedEvent.RefundTimestamp}");
                }

                if (initiatedEvent.Countdown != lockTimeInSec)  //todo: use it
                {
                    Log.Debug(
                        "Invalid countdown in initiated event. Expected value is {@expected}, actual is {@actual}",
                        lockTimeInSec,
                        (long)initiatedEvent.Countdown);

                    return new Error(
                        code: Errors.InvalidRewardForRedeem,
                        message: $"Invalid countdown in initiated event. Expected value is {lockTimeInSec}, actual is {(long)initiatedEvent.Countdown}");
                }

                if (initiatedEvent.RedeemFee != requiredRewardForRedeemInDecimals)
                {
                    Log.Debug(
                        "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                        requiredRewardForRedeemInDecimals,
                        (long)initiatedEvent.RedeemFee);

                    return new Error(
                        code: Errors.InvalidRewardForRedeem,
                        message: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInDecimals}, actual is {(long)initiatedEvent.RedeemFee}");
                }

                if (!initiatedEvent.Active)
                {
                    Log.Debug(
                        "Invalid active value in initiated event. Expected value is {@expected}, actual is {@actual}",
                        true,
                        initiatedEvent.Active);

                    return new Error(
                        code: Errors.InvalidRewardForRedeem,
                        message: $"Invalid active value in initiated event. Expected value is {true}, actual is {initiatedEvent.Active}");
                }

                var erc20TransferValues = await GetTransferValuesAsync(
                        currency: erc20,
                        from: initiatedEvent.Initiator[2..],
                        to: erc20.SwapContractAddress[2..],
                        blockNumber: contractInitEvent.HexBlockNumber,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!erc20TransferValues.Contains(initiatedEvent.Value + initiatedEvent.RedeemFee))
                {
                    var actualTransferValue = string.Join(", ", erc20TransferValues.Select(v => v.ToString()));

                    Log.Debug(
                        "Invalid transfer value in erc20 initiated event. Expected value is {@expected}, actual is {@actual}",
                        initiatedEvent.Value.ToString(),
                        actualTransferValue);

                    return new Error(
                        code: Errors.InvalidSwapPaymentTx,
                        message: $"Invalid transfer value in erc20 initiated event. Expected value is {initiatedEvent.Value}, actual is {actualTransferValue}");
                }

                var receivedAmountInDecimals = initiatedEvent.Value;

                if (receivedAmountInDecimals >= requiredAmountInDecimals - requiredRewardForRedeemInDecimals)
                    return true;

                Log.Debug(
                    "Ethereum ERC20 value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                    (decimal)(requiredAmountInDecimals - requiredRewardForRedeemInDecimals),
                    (decimal)initiatedEvent.Value);

                var (addEvents, addError) = await api
                    .GetContractEventsAsync(
                        address: erc20.SwapContractAddress,
                        fromBlock: erc20.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<Erc20AddedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (addError != null)
                    return addError;

                events = addEvents;

                if (events == null || !events.Any())
                    return false;

                foreach (var @event in events.Select(e => e.ParseErc20AddedEvent()))
                {
                    erc20TransferValues = await GetTransferValuesAsync(
                            currency: erc20,
                            from: @event.Initiator[2..],
                            to: erc20.SwapContractAddress[2..],
                            blockNumber: contractInitEvent.HexBlockNumber,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (!erc20TransferValues.Contains(@event.Value - receivedAmountInDecimals))
                    {
                        var actualTransferValue = string.Join(", ", erc20TransferValues.Select(v => v.ToString()));

                        Log.Debug(
                            "Invalid transfer value in added event. Expected value is {@expected}, actual is {@actual}",
                            (@event.Value - receivedAmountInDecimals).ToString(),
                            actualTransferValue);

                        return new Error(
                            code: Errors.InvalidSwapPaymentTx,
                            message: $"Invalid transfer value in initiated event. Expected value is {@event.Value - receivedAmountInDecimals}, actual is {actualTransferValue}");
                    }

                    receivedAmountInDecimals = @event.Value;

                    if (receivedAmountInDecimals >= requiredAmountInDecimals - requiredRewardForRedeemInDecimals)
                        return true;

                    Log.Debug(
                        "Ethereum ERC20 value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                        requiredAmountInDecimals - requiredRewardForRedeemInDecimals,
                        (long)@event.Value);
                }

            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum ERC20 swap initiated control task error");

                return new Error(Errors.InternalError, e.Message);
            }

            return false;
        }

        public static async Task<List<BigInteger>> GetTransferValuesAsync(
            CurrencyConfig currency,
            string from,
            string to,
            string blockNumber,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum ERC20: check transfer event");

                var erc20 = (Erc20Config)currency;

                var api = erc20.GetEtherScanApi();

                var (events, error) = await api
                    .GetContractEventsAsync(
                        address: erc20.ERC20ContractAddress,
                        fromBlock: (ulong) new HexBigInteger(blockNumber).Value,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<Erc20TransferEventDTO>(),
                        topic1: "0x000000000000000000000000" + from,
                        topic2: "0x000000000000000000000000" + to,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return new List<BigInteger>();

                if (events == null || !events.Any())
                    return new List<BigInteger>();

                return events
                    .Select(e => e.ParseErc20TransferEvent().Value)
                    .ToList();
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum ERC20 get transfer value task error");

                return new List<BigInteger>();
            }
        }

        public static async Task StartSwapInitiatedControlAsync(
            Swap swap,
            Erc20Config erc20Config,
            long lockTimeInSec,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} started", erc20Config.Name, swap.Id);

            try
            {
                var refundTimeStamp = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSec));

                while (!cancellationToken.IsCancellationRequested)
                {

                    if (swap.IsCanceled || DateTimeOffset.UtcNow >= refundTimeStamp)
                    {
                        await canceledHandler
                            .Invoke(swap, cancellationToken)
                            .ConfigureAwait(false);

                        break;
                    }

                    var (isInitiated, error) = await IsInitiatedAsync(
                            swap: swap,
                            erc20: erc20Config,
                            lockTimeInSec: lockTimeInSec,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (error != null)
                    {
                        Log.Error("{@currency} IsInitiatedAsync error for swap {@swap}. Code: {@code}. Message: {@desc}",
                            erc20Config.Name,
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
                    erc20Config.Name,
                    swap.Id,
                    cancellationToken.IsCancellationRequested ? "canceled" : "completed");
            }
            catch (OperationCanceledException)
            {
                Log.Debug("StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} canceled",
                    erc20Config.Name,
                    swap.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "StartSwapInitiatedControlAsync for {@Currency} swap with id {@swapId} error",
                    erc20Config.Name,
                    swap.Id);
            }
        }
    }
}