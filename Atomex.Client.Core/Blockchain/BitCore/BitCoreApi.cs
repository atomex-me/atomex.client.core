using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;

namespace Atomex.Blockchain.BitCore
{
    public class BitCoreApi : BitcoinBasedBlockchainApi
    {
        internal class RawTx
        {
            [JsonProperty(PropertyName = "rawTx")]
            public string RawTxHex { get; set; }
        }

        internal class SendTxId
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
        }

        internal class AddressBalance
        {
            [JsonProperty(PropertyName = "confirmed")]
            public long Confirmed { get; set; }

            [JsonProperty(PropertyName = "unconfirmed")]
            public long Unconfirmed { get; set; }

            [JsonProperty(PropertyName = "balance")]
            public long Balance { get; set; }
        }

        internal class Tx
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }

            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }

            [JsonProperty(PropertyName = "chain")]
            public string Chain { get; set; }

            [JsonProperty(PropertyName = "blockHeight")]
            public int? BlockHeight { get; set; }

            [JsonProperty(PropertyName = "blockHash")]
            public string BlockHash { get; set; }

            [JsonProperty(PropertyName = "blockTime")]
            public DateTime BlockTime { get; set; }

            [JsonProperty(PropertyName = "size")]
            public int Size { get; set; }

            [JsonProperty(PropertyName = "fee")]
            public long Fee { get; set; }

            [JsonProperty(PropertyName = "value")]
            public long Value { get; set; }

            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }

            [JsonProperty(PropertyName = "coins")]
            public Coins Coins { get; set; }
        }

        internal class Coins
        {
            [JsonProperty(PropertyName = "inputs")]
            public List<Output> Inputs { get; set; }

            [JsonProperty(PropertyName = "outputs")]
            public List<Output> Outputs { get; set; }
        }

        internal class Output
        {
            [JsonProperty(PropertyName = "chain")]
            public string Chain { get; set; }

            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }

            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }

            [JsonProperty(PropertyName = "mintTxid")]
            public string TxId { get; set; }

            [JsonProperty(PropertyName = "mintIndex")]
            public uint Index { get; set; }

            [JsonProperty(PropertyName = "spentTxid")]
            public string SpentTxId { get; set; }

            [JsonProperty(PropertyName = "script")]
            public string Script { get; set; }

            [JsonProperty(PropertyName = "value")]
            public decimal Value { get; set; }

            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
        }

        public const string BitCoreBaseUri = "https://api.bitcore.io/";

        private const int MinDelayBetweenRequestMs = 1000;

        private string BaseUri { get; }

        private static readonly RequestLimitControl RequestLimitControl
            = new (MinDelayBetweenRequestMs);

        public BitcoinBasedConfig Currency { get; }
        public string Network => Currency.Network == NBitcoin.Network.Main ? "mainnet" : "testnet";

        public BitCoreApi(BitcoinBasedConfig currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = configuration["BlockchainApiBaseUri"];
        }

        public BitCoreApi(BitcoinBasedConfig currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        }

        public override async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/{Currency.Name}/{Network}/address/{address}/balance/";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) => JsonConvert.DeserializeObject<AddressBalance>(content).Balance,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var txHexResult = await GetRawTxAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (txHexResult == null)
                return new Error(Errors.RequestError, "Connection error while getting tx");

            if (txHexResult.HasError)
                return txHexResult.Error;

            var requestUri = $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/tx/{txId}";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<IBlockchainTransaction>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(content);

                        return new BitcoinBasedTransaction(
                            currency: Currency.Name,
                            tx: Transaction.Parse(txHexResult.Value, Currency.Network),
                            blockInfo: new BlockInfo
                            {
                                Confirmations = tx.Confirmations,
                                BlockHeight = tx.BlockHeight.GetValueOrDefault(0),
                                FirstSeen = tx.BlockTime,
                                BlockTime = tx.BlockTime
                            },
                            fees: tx.Fee
                        );
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var tx = (BitcoinBasedTransaction)transaction;
            var txHex = tx.ToBytes().ToHexString();

            tx.State = BlockchainTransactionState.Pending;

            var requestUri = $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/tx/send";
            var requestContent = JsonConvert.SerializeObject(new RawTx {RawTxHex = txHex});

            return await HttpHelper.PostAsyncResult<string>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    content: new StringContent(requestContent, Encoding.UTF8, "application/json"),
                    responseHandler: (response, content) => JsonConvert.DeserializeObject<SendTxId>(content).TxId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async override Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default)
        {
            var txHexResult = await GetRawTxAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (txHexResult == null)
                return new Error(Errors.RequestError, "Connection error while getting input");

            if (txHexResult.HasError)
                return txHexResult.Error;

            var tx = Transaction.Parse(txHexResult.Value, Currency.Network);
            var txInput = tx.Inputs.AsIndexedInputs().ToList()[(int) inputNo];

            return new BitcoinBasedTxPoint(txInput);
        }

        public async override Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/address/{address}/?unspent=true";

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var outputs = JsonConvert.DeserializeObject<List<Output>>(content);

                        return new Result<IEnumerable<BitcoinBasedTxOutput>>(outputs.Select(o => new BitcoinBasedTxOutput(
                            coin: new Coin(
                                fromTxHash: new uint256(o.TxId),
                                fromOutputIndex: o.Index,
                                amount: new Money(o.Value, MoneyUnit.Satoshi),
                                scriptPubKey: Script.FromHex(o.Script)),
                            confirmations: o.Confirmations,
                            spentTxPoint: null,
                            spentTxConfirmations: 0)));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async override Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/address/{address}";

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var outputs = JsonConvert.DeserializeObject<List<Output>>(content);

                        return new Result<IEnumerable<BitcoinBasedTxOutput>>(outputs.Select(o =>
                        {
                            var spentPoint = !string.IsNullOrEmpty(o.SpentTxId)
                                ? new TxPoint(0, o.SpentTxId)
                                : null;

                            return new BitcoinBasedTxOutput(
                                coin: new Coin(
                                    fromTxHash: new uint256(o.TxId),
                                    fromOutputIndex: o.Index,
                                    amount: new Money(o.Value, MoneyUnit.Satoshi),
                                    scriptPubKey: Script.FromHex(o.Script)),
                                confirmations: o.Confirmations,
                                spentTxPoint: spentPoint,
                                spentTxConfirmations: 0); // TODO: get spent tx confiramtions
                        }));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async override Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var txResult = await HttpHelper.GetAsyncResult<Tx>(
                    baseUri: BaseUri,
                    requestUri: $"api/{Currency.Name}/{Currency.Network.ToString().ToLower()}/tx/{txId}/populated",
                    responseHandler: (response, content) => JsonConvert.DeserializeObject<Tx>(content),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txResult == null)
                return new Error(Errors.RequestError, "Connection error while getting output spent point");

            if (txResult.HasError)
                return txResult.Error;

            var spentTxId = txResult.Value
                ?.Coins
                ?.Outputs
                ?.FirstOrDefault(o => o.Index == outputNo)
                ?.SpentTxId;

            if (string.IsNullOrEmpty(spentTxId))
                return new Result<ITxPoint>((ITxPoint)null);

            var spentTxHexResult = await GetRawTxAsync(spentTxId, cancellationToken)
                .ConfigureAwait(false);

            if (spentTxHexResult == null)
                return new Error(Errors.RequestError, "Connection error while getting raw tx");

            if (spentTxHexResult.HasError)
                return spentTxHexResult.Error;

            var spentTx = Transaction.Parse(spentTxHexResult.Value, Currency.Network);

            var spentTxInputs = spentTx.Inputs.AsIndexedInputs().ToList();

            for (var i = 0; i < spentTxInputs.Count; ++i)
            {
                if (spentTxInputs[i].PrevOut.Hash.ToString() == txId &&
                    spentTxInputs[i].PrevOut.N == outputNo)
                    return new TxPoint((uint)i, spentTxId);
            }

            return new Result<ITxPoint>((ITxPoint)null);
        }

        private async Task<Result<string>> GetRawTxAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            // todo: change to rpc node!
            const string baseUri = "https://api.blockcypher.com/";
            var network = Currency.Network == NBitcoin.Network.Main
                ? "main"
                : "test3";

            var requestUri = $"v1/btc/{network}/txs/{txId}?includeHex=true";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<string>(
                    baseUri: baseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) => JObject.Parse(content)["hex"].ToString(),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}