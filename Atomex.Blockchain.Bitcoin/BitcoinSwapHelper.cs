using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;

using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Blockchain.Bitcoin.Common;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public static class BitcoinSwapHelper
    {
        public static BitcoinTransaction CreateLockTransaction(
            IEnumerable<BitcoinTxOutput> outputs,
            long amount,
            long fee,
            string secretHash,
            string address,
            string refundAddress,
            string changeAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            Network network)
        {
            var currency = outputs.First().Currency;

            var lockScript = BitcoinSwapTemplate.CreateHtlcSwapLockScript(
                aliceRefundAddress: refundAddress,
                bobAddress: address,
                lockTimeStamp: (long)(timeStamp + lockTime),
                secretHash: Hex.FromString(secretHash),
                secretSize: secretSize,
                network: network);

            var lockScriptHash = address.IsSegWitAddress(network)
                ? lockScript.WitHash.ScriptPubKey
                : lockScript.Hash.ScriptPubKey;

            return BitcoinTransaction.Create(
                currency: currency,
                coins: outputs.Select(o => o.Coin),
                recipients: new BitcoinDestination[]
                {
                    new BitcoinDestination
                    {
                        Script = lockScriptHash,
                        AmountInSatoshi = amount
                    }
                },
                change: changeAddress.GetAddressScript(network),
                feeInSatoshi: fee,
                lockTime: DateTimeOffset.MinValue,
                network: network,
                knownRedeems: lockScript);
        }

        public static async Task<(IEnumerable<BitcoinTransaction> txs, Error error)> FindLocksAsync(
            IBitcoinApi bitcoinApi,
            string secretHash,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            Network network,
            CancellationToken cancellationToken = default)
        {
            var lockScript = BitcoinSwapTemplate.CreateHtlcSwapLockScript(
                aliceRefundAddress: refundAddress,
                bobAddress: address,
                lockTimeStamp: (long)(timeStamp + lockTime),
                secretHash: Hex.FromString(secretHash),
                secretSize: secretSize,
                network: network);

            var lockAddress = address.IsSegWitAddress(network)
                ? lockScript.WitHash.ScriptPubKey
                    .GetDestinationAddress(network)
                    .ToString()
                : lockScript.Hash.ScriptPubKey
                    .GetDestinationAddress(network)
                    .ToString();

            var (outputs, outputsError) = await bitcoinApi
                .GetOutputsAsync(lockAddress, cancellationToken)
                .ConfigureAwait(false);

            if (outputsError != null)
                return (txs: null, error: outputsError);

            var txs = new Dictionary<string, BitcoinTransaction>();

            foreach (var output in outputs)
            {
                // skip other outputs
                if (output.Address != lockAddress)
                    continue;

                // skip already founded txs
                if (txs.ContainsKey(output.TxId))
                    continue;

                var (tx, txError) = await bitcoinApi
                    .GetTransactionAsync(output.TxId, cancellationToken)
                    .ConfigureAwait(false);

                if (txError != null)
                    return (txs: null, error: txError);

                txs.Add(output.TxId, tx as BitcoinTransaction);
            }

            return (txs: txs.Values, error: null);
        }

        public static Task<(IEnumerable<BitcoinTransaction> txs, Error error)> FindRedeemsAsync(
            IBitcoinApi bitcoinApi,
            string secretHash,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            Network network,
            CancellationToken cancellationToken = default)
        {
            return FindSpentsAsync(
                bitcoinApi: bitcoinApi,
                secretHash: secretHash,
                inputFilter: i => BitcoinSwapTemplate.IsSwapRedeem(Script.FromHex(i.ScriptSig)),
                address: address,
                refundAddress: refundAddress,
                timeStamp: timeStamp,
                lockTime: lockTime,            
                secretSize: secretSize,
                network: network,
                cancellationToken: cancellationToken);
        }

        public static Task<(IEnumerable<BitcoinTransaction> txs, Error error)> FindRefundsAsync(
            IBitcoinApi bitcoinApi,
            string secretHash,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            Network network,
            CancellationToken cancellationToken = default)
        {
            return FindSpentsAsync(
                bitcoinApi: bitcoinApi,
                secretHash: secretHash,
                inputFilter: i => BitcoinSwapTemplate.IsSwapRefund(Script.FromHex(i.ScriptSig)),
                address: address,
                refundAddress: refundAddress,
                timeStamp: timeStamp,
                lockTime: lockTime,
                secretSize: secretSize,
                network: network,
                cancellationToken: cancellationToken);
        }

        public static async Task<(IEnumerable<BitcoinTransaction> txs, Error error)> FindSpentsAsync(
            IBitcoinApi bitcoinApi,
            string secretHash,
            Func<BitcoinTxInput, bool> inputFilter,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,           
            int secretSize,
            Network network,
            CancellationToken cancellationToken = default)
        {
            var lockScript = BitcoinSwapTemplate.CreateHtlcSwapLockScript(
                aliceRefundAddress: refundAddress,
                bobAddress: address,
                lockTimeStamp: (long)(timeStamp + lockTime),
                secretHash: Hex.FromString(secretHash),
                secretSize: secretSize,
                network: network);

            var lockAddress = address.IsSegWitAddress(network)
                ? lockScript.WitHash.ScriptPubKey
                    .GetDestinationAddress(network)
                    .ToString()
                : lockScript.Hash.ScriptPubKey
                    .GetDestinationAddress(network)
                    .ToString();

            var (outputs, outputsError) = await bitcoinApi
                .GetOutputsAsync(lockAddress, cancellationToken)
                .ConfigureAwait(false);

            if (outputsError != null)
                return (txs: null, error: outputsError);

            var txs = new Dictionary<string, BitcoinTransaction>();

            foreach (var output in outputs)
            {
                // skip other outputs
                if (output.Address != lockAddress)
                    continue;

                // skip unspent outputs
                if (output.SpentTxPoints == null || !output.SpentTxPoints.Any())
                    continue;

                foreach (var spentTxPoint in output.SpentTxPoints)
                {
                    // skip already founded txs
                    if (txs.ContainsKey(spentTxPoint.Hash))
                        continue;

                    var (tx, txError) = await bitcoinApi
                        .GetTransactionAsync(spentTxPoint.Hash, cancellationToken)
                        .ConfigureAwait(false);

                    if (txError != null)
                        return (txs: null, error: txError);

                    var spentTx = tx as BitcoinTransaction;

                    var spentInput = spentTx.Inputs
                        .First(i => i.PreviousOutput.Index == output.Index);

                    if (inputFilter?.Invoke(spentInput) ?? true)
                        txs.TryAdd(spentTx.TxId, spentTx);
                }
            }

            return (txs: txs.Values, error: null);
        }
    }
}