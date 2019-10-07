using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Info.Blockchain.API.BlockExplorer;
using Info.Blockchain.API.Client;
using Info.Blockchain.API.PushTx;
using NBitcoin;
using Serilog;
using Network = NBitcoin.Network;
using Transaction = NBitcoin.Transaction;

namespace Atomex.Blockchain.BlockchainInfo
{
    public class BlockchainInfoApi : IInOutBlockchainApi
    {
        private readonly BitcoinBasedCurrency _currency;
        private readonly BlockchainHttpClient _client;
        private readonly BlockExplorer _explorer;

        private readonly string _apiCode = "64e1d1c5-ed33-4fd6-ab77-81e650343f05";

        public BlockchainInfoApi(BitcoinBasedCurrency currency)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));

            var baseUrl = _currency.Network == Network.Main
                ? "https://blockchain.info/"
                : "https://testnet.blockchain.info/";

            _client = new BlockchainHttpClient(apiCode: _apiCode, uri: baseUrl);
            _explorer = new BlockchainApiHelper(apiCode: _apiCode, baseHttpClient: new BlockchainHttpClient(uri: baseUrl)).blockExplorer;
        }

        public async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var addressInfo = await _explorer
                    .GetBase58AddressAsync(address)
                    .ConfigureAwait(false);

                return new Result<decimal>(addressInfo.FinalBalance.GetBtc());
            }
            catch (Exception e)
            {
                Log.Warning("Server api exception while get address {@address}: {@message}",
                    address,
                    ResolveExceptionMessage(e));

                return new Result<decimal>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        public async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var tx = await _explorer
                    .GetTransactionByHashAsync(txId)
                    .ConfigureAwait(false);

                var result = new BitcoinBasedTransaction(
                    currency: _currency,
                    tx: Transaction.Parse(await GetTxHexAsync(txId).ConfigureAwait(false), _currency.Network),
                    blockInfo: new BlockInfo
                    {
                        Confirmations = (int)(await GetBlockCountAsync().ConfigureAwait(false) - tx.BlockHeight + 1),
                        BlockHash = null,
                        BlockHeight = tx.BlockHeight,
                        FirstSeen = tx.Time,
                        BlockTime = tx.Time
                    },
                    fees: await GetTxFeeAsync(txId).ConfigureAwait(false));

                return new Result<IBlockchainTransaction>(result);
            }
            catch (Exception e)
            {
                Log.Warning("Server api exception while get transaction by id {@txId}: {@message}",
                    txId,
                    ResolveExceptionMessage(e));

                return new Result<IBlockchainTransaction>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        public async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var tx = (IBitcoinBasedTransaction)transaction;
                var txHex = tx.ToBytes().ToHexString();

                Log.Debug("TxHex: {@txHex}", txHex);

                tx.State = BlockchainTransactionState.Pending;

                await new TransactionPusher()
                    .PushTransactionAsync(txHex)
                    .ConfigureAwait(false);

                return new Result<string>(tx.Id); // todo: receive id from network!!!
            }
            catch (Exception e)
            {
                return new Result<string>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        public async Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var tx = await _explorer
                    .GetTransactionByHashAsync(txId)
                    .ConfigureAwait(false);

                var input = tx.Inputs[(int)inputNo];

                var prevTx = await _explorer
                    .GetTransactionByIndexAsync(input.PreviousOutput.TxIndex)
                    .ConfigureAwait(false);

                var txIn = new TxIn(
                    prevout: new OutPoint(
                        hashIn: new uint256(prevTx.Hash),
                        nIn: input.PreviousOutput.N),
                    scriptSig: new Script(input.ScriptSignature));

                return new Result<ITxPoint>(new BitcoinBasedTxPoint(new IndexedTxIn
                {
                    Index = inputNo,
                    TxIn = txIn,
                    WitScript = new WitScript(input.Witness)
                }));
            }
            catch (Exception e)
            {
                return new Result<ITxPoint>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        //public async Task<IEnumerable<ITxPoint>> GetInputsAsync(
        //    string txId,
        //    CancellationToken cancellationToken = default(CancellationToken))
        //{
        //    var result = new List<ITxPoint>();
        //    var tx = await _explorer
        //        .GetTransactionByHashAsync(txId)
        //        .ConfigureAwait(false);

        //    foreach (var i in tx.Inputs)
        //    {
        //        var witScript = WitScript.Empty;
        //        var prevTx = await _explorer
        //            .GetTransactionByIndexAsync(i.PreviousOutput.TxIndex)
        //            .ConfigureAwait(false);

        //        if (i.Witness != null)
        //        {
        //            var wit = new List<string> {i.Witness};
        //            witScript = wit.Aggregate(witScript, (current, witness) => current + new WitScript(witness));
        //        }
        //        result.Add(new BitcoinBasedTxPoint(new IndexedTxIn
        //        {
        //            TxIn = new TxIn(new OutPoint(new uint256(prevTx.Hash), i.PreviousOutput.N), new Script()),
        //            Index = (uint)i.PreviousOutput.SpendingOutpoints[0].N,
        //            WitScript = witScript,
        //        }));
        //    }

        //    return result;
        //}

        public async Task<Result<IEnumerable<ITxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var unspentOutputs = await _explorer
                    .GetUnspentOutputsAsync(new List<string> { address })
                    .ConfigureAwait(false);

                var result = unspentOutputs.Select(u => new BitcoinBasedTxOutput(
                    coin: new Coin(
                        fromTxHash: new uint256(u.TransactionHash),
                        fromOutputIndex: (uint) u.N,
                        amount: new Money(u.Value.Satoshis, MoneyUnit.Satoshi),
                        scriptPubKey: new Script(u.Script)),
                    spentTxPoint: null));

                return new Result<IEnumerable<ITxOutput>>(result);
            }
            catch (Exception e)
            {
                return new Result<IEnumerable<ITxOutput>>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        public async Task<Result<IEnumerable<ITxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var data = await _explorer
                    .GetHash160AddressAsync(address)
                    .ConfigureAwait(false);

                var outputs = new List<ITxOutput>();

                foreach (var tx in data.Transactions)
                {
                    foreach (var output in tx.Outputs)
                    {
                        if (output.Address == null ||
                            !output.Address.ToLowerInvariant().Equals(address.ToLowerInvariant()))
                            continue;

                        var spentTxPoint = output.Spent
                            ? new TxPoint(output.SpendingOutpoints[0].N,
                                (await _explorer.GetTransactionByIndexAsync(output.SpendingOutpoints[0].TxIndex)
                                    .ConfigureAwait(false)).Hash)
                            : null;

                        var amount = new Money(output.Value.Satoshis, MoneyUnit.Satoshi);

                        var script = new Script(Hex.FromString(output.Script));

                        outputs.Add(new BitcoinBasedTxOutput(
                            coin: new Coin(
                                fromTxHash: new uint256(tx.Hash),
                                fromOutputIndex: (uint)output.N,
                                amount: amount,
                                scriptPubKey: script),
                            spentTxPoint: spentTxPoint));
                    }
                }

                return new Result<IEnumerable<ITxOutput>>(outputs);
            }
            catch (Exception e)
            {
                return new Result<IEnumerable<ITxOutput>>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        public async Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var tx = await _explorer
                    .GetTransactionByHashAsync(txId)
                    .ConfigureAwait(false);

                if (tx.Outputs[(int)outputNo] == null)
                    return null;

                var result = tx.Outputs[(int)outputNo].Spent
                    ? new TxPoint(tx.Outputs[(int)outputNo].SpendingOutpoints[0].N,
                        (await _explorer.GetTransactionByIndexAsync(tx.Outputs[(int)outputNo].SpendingOutpoints[0].TxIndex).ConfigureAwait(false)).Hash)
                    : null;

                return new Result<ITxPoint>(result);
            }
            catch (Exception e)
            {
                return new Result<ITxPoint>(new Error(Errors.RequestError, ResolveExceptionMessage(e)));
            }
        }

        private Task<string> GetTxHexAsync(string txId)
        {
            if (string.IsNullOrWhiteSpace(txId))
                throw new ArgumentNullException(nameof(txId));

            return _client.GetStringAsync($"rawtx/{txId}?format=hex");
        }

        private Task<long> GetTxFeeAsync(string txId)
        {
            if (string.IsNullOrWhiteSpace(txId))
                throw new ArgumentNullException(nameof(txId));

            return _client.GetLongAsync($"/q/txfee/{txId}");
        }

        private Task<int> GetBlockCountAsync()
        {
            return _client.GetIntAsync("/q/getblockcount");
        }

        private static string ResolveExceptionMessage(Exception e)
        {
            if (e.Message.Contains("Gateway Timeout:"))
                return "Gateway Timeout";

            if (e.Message.Contains("Origin Connection Time-out:"))
                return "Origin Connection Time-out";

            return e.Message.Contains("Bad Gateway:")
                ? "Bad Gateway"
                : e.Message;
        }
    }
}