using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Hex.HexTypes;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;
using System.Collections.Generic;

namespace Atomex.Swaps.Ethereum.ERC20.Helpers
{
    public static class ERC20SwapInitiatedHelper
    {
        private const int BlocksAhead = 10000; // >24h with block time 10-30 seconds

        public static async Task<Result<IBlockchainTransaction>> TryToFindPaymentAsync(
            Swap swap,
            CurrencyConfig currency,
            CancellationToken cancellationToken = default)
        {
            var erc20 = currency as EthereumTokens.Erc20Config;

            var api = erc20.BlockchainApi as IEthereumBlockchainApi;

            var blockNoResult = await api
                .TryGetBlockByTimeStampAsync(swap.TimeStamp.ToUniversalTime().ToUnixTime(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (blockNoResult == null)
                return new Error(Errors.RequestError, "Can't get Ethereum block number by timestamp");

            if (blockNoResult.HasError)
                return blockNoResult.Error;

            var txsResult = await api
                .TryGetTransactionsAsync(
                    address: erc20.SwapContractAddress,
                    fromBlock: blockNoResult.Value,
                    toBlock: blockNoResult.Value + BlocksAhead,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
                return new Error(Errors.RequestError, "Can't get Ethereum swap contract transactions");

            if (txsResult.HasError)
                return txsResult.Error;

            var savedTx = swap.PaymentTx as EthereumTransaction;

            foreach (var tx in txsResult.Value.Cast<EthereumTransaction>())
            {
                if (tx.Amount != 0 ||
                   !tx.Input.Equals(savedTx.Input, StringComparison.OrdinalIgnoreCase) ||
                   !tx.From.Equals(savedTx.From, StringComparison.OrdinalIgnoreCase) ||
                   !tx.To.Equals(erc20.SwapContractAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (tx.State == BlockchainTransactionState.Failed)
                    continue; // skip failed transactions

                return tx;
            }

            return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);
        }

        public static async Task<Result<bool>> IsInitiatedAsync(
            Swap swap,
            CurrencyConfig currency,
            long lockTimeInSec,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log.Debug("Ethereum ERC20: check initiated event");

                var erc20 = (EthereumTokens.Erc20Config)currency;

                var side = swap.Symbol
                    .OrderSideForBuyCurrency(swap.PurchasedCurrency)
                    .Opposite();

                var refundTimeStamp = new DateTimeOffset(swap.TimeStamp.ToUniversalTime().AddSeconds(lockTimeInSec)).ToUnixTimeSeconds();
                var requiredAmountInERC20 = AmountHelper.QtyToAmount(side, swap.Qty, swap.Price, erc20.DigitsMultiplier);
                var requiredAmountInDecimals = erc20.TokensToTokenDigits(requiredAmountInERC20);
                var receivedAmountInDecimals = new BigInteger(0);
                var requiredRewardForRedeemInDecimals = swap.IsAcceptor
                    ? erc20.TokensToTokenDigits(swap.RewardForRedeem)
                    : 0;

                var api = new EtherScanApi(erc20);

                var initiateEventsResult = await api
                    .GetContractEventsAsync(
                        address: erc20.SwapContractAddress,
                        fromBlock: erc20.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<ERC20InitiatedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        topic2: "0x000000000000000000000000" + erc20.ERC20ContractAddress.Substring(2),  //??
                        topic3: "0x000000000000000000000000" + swap.ToAddress.Substring(2),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (initiateEventsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while trying to get contract {erc20.SwapContractAddress} initiate event");

                if (initiateEventsResult.HasError)
                    return initiateEventsResult.Error;

                var events = initiateEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return false;

                var contractInitEvent = events.Last();

                var initiatedEvent = contractInitEvent.ParseERC20InitiatedEvent();

                if (initiatedEvent.RefundTimestamp != refundTimeStamp)
                {
                    Log.Debug(
                        "Invalid refund time in initiated event. Expected value is {@expected}, actual is {@actual}",
                        refundTimeStamp,
                        (long)initiatedEvent.RefundTimestamp);

                    return new Error(
                        code: Errors.InvalidRefundLockTime,
                        description: $"Invalid refund time in initiated event. Expected value is {refundTimeStamp}, actual is {(long)initiatedEvent.RefundTimestamp}");
                }

                if (initiatedEvent.Countdown != lockTimeInSec)  //todo: use it
                {
                    Log.Debug(
                        "Invalid countdown in initiated event. Expected value is {@expected}, actual is {@actual}",
                        lockTimeInSec,
                        (long)initiatedEvent.Countdown);

                    return new Error(
                        code: Errors.InvalidRewardForRedeem,
                        description: $"Invalid countdown in initiated event. Expected value is {lockTimeInSec}, actual is {(long)initiatedEvent.Countdown}");
                }

                if (initiatedEvent.RedeemFee != requiredRewardForRedeemInDecimals)
                {
                    Log.Debug(
                        "Invalid redeem fee in initiated event. Expected value is {@expected}, actual is {@actual}",
                        requiredRewardForRedeemInDecimals,
                        (long)initiatedEvent.RedeemFee);

                    return new Error(
                        code: Errors.InvalidRewardForRedeem,
                        description: $"Invalid redeem fee in initiated event. Expected value is {requiredRewardForRedeemInDecimals}, actual is {(long)initiatedEvent.RedeemFee}");
                }

                if (!initiatedEvent.Active)
                {
                    Log.Debug(
                        "Invalid active value in initiated event. Expected value is {@expected}, actual is {@actual}",
                        true,
                        initiatedEvent.Active);

                    return new Error(
                        code: Errors.InvalidRewardForRedeem,
                        description: $"Invalid active value in initiated event. Expected value is {true}, actual is {initiatedEvent.Active}");
                }

                var erc20TransferValues = await GetTransferValuesAsync(
                        currency: currency,
                        from: initiatedEvent.Initiator.Substring(2),
                        to: erc20.SwapContractAddress.Substring(2),
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
                        description: $"Invalid transfer value in erc20 initiated event. Expected value is {initiatedEvent.Value}, actual is {actualTransferValue}");
                }

                receivedAmountInDecimals = initiatedEvent.Value;

                if (receivedAmountInDecimals >= requiredAmountInDecimals - requiredRewardForRedeemInDecimals)
                    return true;

                Log.Debug(
                    "Ethereum ERC20 value is not enough. Expected value is {@expected}. Actual value is {@actual}",
                    (decimal)(requiredAmountInDecimals - requiredRewardForRedeemInDecimals),
                    (decimal)initiatedEvent.Value);

                var addEventsResult = await api
                    .GetContractEventsAsync(
                        address: erc20.SwapContractAddress,
                        fromBlock: erc20.SwapContractBlockNumber,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<ERC20AddedEventDTO>(),
                        topic1: "0x" + swap.SecretHash.ToHexString(),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (addEventsResult == null)
                    return new Error(Errors.RequestError, $"Connection error while trying to get contract {erc20.SwapContractAddress} add event");

                if (addEventsResult.HasError)
                    return addEventsResult.Error;

                events = addEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return false;

                foreach (var @event in events.Select(e => e.ParseERC20AddedEvent()))
                {
                    erc20TransferValues = await GetTransferValuesAsync(
                            currency: currency,
                            from: @event.Initiator.Substring(2),
                            to: erc20.SwapContractAddress.Substring(2),
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
                            description: $"Invalid transfer value in initiated event. Expected value is {@event.Value - receivedAmountInDecimals}, actual is {actualTransferValue}");
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

                var erc20 = (EthereumTokens.Erc20Config)currency;

                var api = new EtherScanApi(erc20);

                var transferEventsResult = await api
                    .GetContractEventsAsync(
                        address: erc20.ERC20ContractAddress,
                        fromBlock: (ulong) new HexBigInteger(blockNumber).Value,
                        toBlock: ulong.MaxValue,
                        topic0: EventSignatureExtractor.GetSignatureHash<ERC20TransferEventDTO>(),
                        topic1: "0x000000000000000000000000" + from,
                        topic2: "0x000000000000000000000000" + to,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (transferEventsResult == null)
                    return new List<BigInteger>();

                if (transferEventsResult.HasError)
                    return new List<BigInteger>();

                var events = transferEventsResult.Value?.ToList();

                if (events == null || !events.Any())
                    return new List<BigInteger>();

                return events.Select(e => e.ParseERC20TransferEvent().Value).ToList();
            }
            catch (Exception e)
            {
                Log.Error(e, "Ethereum ERC20 get transfer value task error");

                return new List<BigInteger>();
            }
        }

        public static Task StartSwapInitiatedControlAsync(
            Swap swap,
            CurrencyConfig currency,
            long lockTimeInSec,
            TimeSpan interval,
            Func<Swap, CancellationToken, Task> initiatedHandler,
            Func<Swap, CancellationToken, Task> canceledHandler,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var isInitiatedResult = await IsInitiatedAsync(
                                swap: swap,
                                currency: currency,
                                lockTimeInSec: lockTimeInSec,
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (isInitiatedResult.HasError)
                        {
                            if (isInitiatedResult.Error.Code != Errors.RequestError)
                            {
                                await canceledHandler.Invoke(swap, cancellationToken)
                                    .ConfigureAwait(false);

                                break;
                            }
                        }
                        else if (isInitiatedResult.Value)
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