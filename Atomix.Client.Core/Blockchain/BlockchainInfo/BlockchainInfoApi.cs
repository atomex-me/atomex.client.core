using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Blockchain.SoChain;
using Atomix.Common;
using Info.Blockchain.API.BlockExplorer;
using Info.Blockchain.API.Client;
using NBitcoin;
using Transaction = NBitcoin.Transaction;

namespace Atomix.Blockchain.BlockchainInfo
{
    public class BlockchainInfoApi : IInOutBlockchainApi
    {
        private readonly BitcoinBasedCurrency _currency;
        private readonly BlockchainHttpClient _client;
        private readonly BlockExplorer _explorer;
        
        public BlockchainInfoApi(BitcoinBasedCurrency currency)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));

             var baseUrl = _currency.Network == Network.Main ? "https://blockchain.info/" : "https://testnet.blockchain.info/";
            _client = new BlockchainHttpClient(uri: baseUrl);
            _explorer = new BlockchainApiHelper(baseHttpClient: new BlockchainHttpClient(uri: baseUrl)).blockExplorer;
        }

        public async Task<IBlockchainTransaction> GetTransactionAsync(string txId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = await _explorer.GetTransactionByHashAsync(txId);
            return new BitcoinBasedTransaction(
                currency: _currency,
                tx: Transaction.Parse(await GetTxHexAsync(txId), _currency.Network),
                blockInfo: new BlockInfo
                {
                    Fees = await GetTxFeeAsync(txId),
                    Confirmations = (int) (await GetBlockCountAsync() - tx.BlockHeight + 1),
                    BlockHeight = tx.BlockHeight,
                    FirstSeen = tx.Time,
                    BlockTime = tx.Time
                });
        }

        public async Task<string> BroadcastAsync(IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var soChain = new SoChainApi(_currency);
            return await soChain.BroadcastAsync(transaction, cancellationToken);
        }

        public async Task<ITxPoint> GetInputAsync(string txId, uint inputNo, CancellationToken cancellationToken = default(CancellationToken))
        {
            return (await GetInputsAsync(txId, cancellationToken)).ToList()[(int)inputNo];
        }

        public async Task<IEnumerable<ITxPoint>> GetInputsAsync(string txId, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new List<ITxPoint>();
            var tx = await _explorer.GetTransactionByHashAsync(txId);
            foreach (var i in tx.Inputs)
            {
                var witScript = WitScript.Empty;
                var prevTx = await _explorer.GetTransactionByIndexAsync(i.PreviousOutput.TxIndex);
                if (i.Witness != null)
                {
                    var wit = new List<string> {i.Witness};
                    witScript = wit.Aggregate(witScript, (current, witness) => current + new WitScript(witness));
                }
                result.Add(new BitcoinBasedTxPoint(new IndexedTxIn
                {
                    TxIn = new TxIn(new OutPoint(new uint256(prevTx.Hash), i.PreviousOutput.N), new Script()),
                    Index = (uint)i.PreviousOutput.SpendingOutpoints[0].N,
                    WitScript = witScript,
                }));
            }

            return result;
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(string address, string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentOutputs = await _explorer.GetUnspentOutputsAsync(new List<string>() {address});
            return unspentOutputs.Select(u => new BitcoinBasedTxOutput(
                coin: new Coin(
                    fromTxHash: new uint256(u.TransactionHash),
                    fromOutputIndex: (uint) u.N,
                    amount: new Money(u.Value.Satoshis, MoneyUnit.Satoshi),
                    scriptPubKey: new Script(u.Script)),
                spentTxPoint: null));
        }

        public async Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var data = await _explorer.GetHash160AddressAsync(address);
            var outputs = new List<ITxOutput>();

            foreach (var tx in data.Transactions)
            {
                foreach (var output in tx.Outputs)
                {
                    if (output.Address == null || !output.Address.ToLowerInvariant().Equals(address.ToLowerInvariant()))
                        continue;

                    var spentTxPoint = output.Spent
                    ? new TxPoint((uint)output.SpendingOutpoints[0].N,
                        (await _explorer.GetTransactionByIndexAsync(output.SpendingOutpoints[0].TxIndex)).Hash)
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

            return outputs;
        }

        public async Task<ITxPoint> IsTransactionOutputSpent(string txId, uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = await _explorer.GetTransactionByHashAsync(txId);
            if (tx.Outputs[(int) outputNo] == null)
                return null;
            
            return tx.Outputs[(int) outputNo].Spent
                ? new TxPoint((uint) tx.Outputs[(int) outputNo].SpendingOutpoints[0].N, 
                    (await _explorer.GetTransactionByIndexAsync(tx.Outputs[(int) outputNo].SpendingOutpoints[0].TxIndex)).Hash)
                : null;
        }

        private async Task<string> GetTxHexAsync(string txId)
        {
            if (string.IsNullOrWhiteSpace(txId))
            {
                throw new ArgumentNullException(nameof(txId));
            }
            return await _client.GetStringAsync($"rawtx/{txId}?format=hex");
        }

        private async Task<long> GetTxFeeAsync(string txId)
        {
            if (string.IsNullOrWhiteSpace(txId))
            {
                throw new ArgumentNullException(nameof(txId));
            }

            return await _client.GetLongAsync($"/q/txfee/{txId}");
        }

        private async Task<int> GetBlockCountAsync()
        {
            return await _client.GetIntAsync("/q/getblockcount");
        }
    }
}