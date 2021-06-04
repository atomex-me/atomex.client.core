using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using Newtonsoft.Json;

namespace Atomex.Blockchain.Insight
{
    public class InsightApi : BitcoinBasedBlockchainApi
    {
        internal class RawTx
        {
            [JsonProperty(PropertyName = "rawtx")]
            public string TxInHex { get; set; }
        }

        internal class SendTxId
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
        }

        internal class ScriptSig
        {
            [JsonProperty(PropertyName = "hex")]
            public string Hex { get; set; }

            [JsonProperty(PropertyName = "asm")]
            public string Asm { get; set; }

            [JsonProperty(PropertyName = "addresses")]
            public List<string> Addresses { get; set; }

            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }
        }

        internal class TxVin
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }

            [JsonProperty(PropertyName = "n")]
            public uint N { get; set; }

            [JsonProperty(PropertyName = "vout")]
            public uint VOut { get; set; }

            [JsonProperty(PropertyName = "sequence")]
            public long Sequence { get; set; }

            [JsonProperty(PropertyName = "scriptSig")]
            public ScriptSig ScriptSig { get; set; }

            //[JsonProperty(PropertyName = "witness")]
            //public ScriptSig WitScript { get; set; }
        }

        internal class TxVout
        {
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }

            [JsonProperty(PropertyName = "n")]
            public uint N { get; set; }

            [JsonProperty(PropertyName = "spentTxId")]
            public string SpentTxId { get; set; }

            [JsonProperty(PropertyName = "spentIndex")]
            public uint? SpentIndex { get; set; }

            [JsonProperty(PropertyName = "spentHeight")]
            public long? SpentHeight { get; set; }

            [JsonProperty(PropertyName = "scriptPubKey")]
            public ScriptSig ScriptPubKey { get; set; }
        }

        internal class Tx
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }

            [JsonProperty(PropertyName = "blockheight")]
            public int? BlockHeight { get; set; }

            [JsonProperty(PropertyName = "blocktime")]
            public long BlockTime { get; set; }

            [JsonProperty(PropertyName = "time")]
            public long Time { get; set; }

            [JsonProperty(PropertyName = "locktime")]
            public long LockTime { get; set; }

            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }

            [JsonProperty(PropertyName = "fees")]
            public decimal Fees { get; set; }

            [JsonProperty(PropertyName = "vin")]
            public List<TxVin> Inputs { get; set; }

            [JsonProperty(PropertyName = "vout")]
            public List<TxVout> Outputs { get; set; }
        }

        internal class Txs
        {
            [JsonProperty(PropertyName = "pagesTotal")]
            public int PagesTotal { get; set; }

            [JsonProperty(PropertyName = "txs")]
            public List<Tx> Transactions { get; set; }
        }

        internal class Utxo
        {
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }

            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }

            [JsonProperty(PropertyName = "vout")]
            public uint VOut { get; set; }

            [JsonProperty(PropertyName = "scriptPubKey")]
            public string ScriptPubKey { get; set; }

            [JsonProperty(PropertyName = "amount")]
            public decimal Amount { get; set; }

            [JsonProperty(PropertyName = "satoshis")]
            public long Satoshis { get; set; }

            [JsonProperty(PropertyName = "height")]
            public int Height { get; set; }

            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
        }

        public const string InsightBitPayMainNet = "https://insight.bitpay.com/";
        public const string InsightBitPayTestNet = "https://test-insight.bitpay.com/";

        public const string InsightLiteCoreMainNet = "https://insight.litecore.io/";
        public const string InsightLiteCoreTestNet = "https://testnet.litecore.io/";

        private const int MinDelayBetweenRequestMs = 1000;

        private string BaseUri { get; }

        private static readonly RequestLimitControl RequestLimitControl
            = new RequestLimitControl(MinDelayBetweenRequestMs);

        public BitcoinBasedConfig Currency { get; }

        public InsightApi(BitcoinBasedConfig currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = configuration["BlockchainApiBaseUri"];
        }

        public InsightApi(BitcoinBasedConfig currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        }

        public override async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/addr/{address}/balance";

            return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) => long.Parse(content, CultureInfo.InvariantCulture) / (decimal) Currency.DigitsMultiplier,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var rawTxResult = await GetRawTxAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (rawTxResult == null)
                return new Error(Errors.RequestError, "Connection error while getting tx");

            if (rawTxResult.HasError)
                return rawTxResult.Error;

            if (string.IsNullOrEmpty(rawTxResult.Value))
                return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/tx/{txId}";

            return await HttpHelper.GetAsyncResult<IBlockchainTransaction>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(content);

                        return new BitcoinBasedTransaction(
                            currency: Currency,
                            tx: Transaction.Parse(rawTxResult.Value, Currency.Network),
                            blockInfo: new BlockInfo
                            {
                                Confirmations = tx.Confirmations,
                                BlockHash = null,
                                BlockHeight = tx.BlockHeight.GetValueOrDefault(0),
                                BlockTime = tx.BlockTime.ToUtcDateTime(),
                                FirstSeen = tx.Time.ToUtcDateTime()
                            },
                            fees: (long) (tx.Fees * Currency.DigitsMultiplier)
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
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var tx = (IBitcoinBasedTransaction)transaction;
            var txHex = tx.ToBytes().ToHexString();

            tx.State = BlockchainTransactionState.Pending;

            const string requestUri = "api/tx/send";
            var parameters = new Dictionary<string, string> { { "rawtx", txHex } };
            using var requestContent = new FormUrlEncodedContent(parameters);

            return await HttpHelper.PostAsyncResult<string>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    content: requestContent,
                    responseHandler: (response, responseContent) => JsonConvert.DeserializeObject<SendTxId>(responseContent).TxId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async override Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/tx/{txId}";

            return await HttpHelper.GetAsyncResult<ITxPoint>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(content);

                        var txIn = tx.Inputs[(int)inputNo];

                        var txInput = new BitcoinBasedTxPoint(new IndexedTxIn
                        {
                            TxIn = new TxIn(new OutPoint(new uint256(txIn.TxId), txIn.VOut),
                                Script.FromHex(txIn.ScriptSig.Hex)),
                            Index = txIn.N,
                            //WitScript = null,
                        });

                        return txInput;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async override Task<Result<IEnumerable<ITxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/addr/{address}/utxo";

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var utxoList = JsonConvert.DeserializeObject<List<Utxo>>(content);

                        var outputs = utxoList.Select(u => new BitcoinBasedTxOutput(
                            coin: new Coin(
                                fromTxHash: new uint256(u.TxId),
                                fromOutputIndex: u.VOut,
                                amount: new Money(u.Satoshis, MoneyUnit.Satoshi),
                                scriptPubKey: Script.FromHex(u.ScriptPubKey)),
                            spentTxPoint: null));

                        return new Result<IEnumerable<ITxOutput>>(outputs);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async override Task<Result<IEnumerable<ITxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/txs/?address={address}"; // todo: pagination?

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var txs = JsonConvert.DeserializeObject<Txs>(content);

                        var outputs = new List<ITxOutput>();

                        foreach (var tx in txs.Transactions)
                        {
                            if (string.IsNullOrEmpty(tx.TxId))
                                continue;

                            foreach (var o in tx.Outputs)
                            {
                                if (o.ScriptPubKey == null ||
                                    o.ScriptPubKey.Addresses == null ||
                                    !o.ScriptPubKey.Addresses.Contains(address))
                                    continue;

                                var spentTxPoint = !string.IsNullOrEmpty(o.SpentTxId) && o.SpentIndex != null
                                    ? new TxPoint(o.SpentIndex.Value, o.SpentTxId)
                                    : null;

                                var amountDecimal = decimal.Parse(o.Value, CultureInfo.InvariantCulture);
                                var amount = new Money(amountDecimal, MoneyUnit.BTC);

                                var script = Script.FromHex(o.ScriptPubKey.Hex);

                                outputs.Add(new BitcoinBasedTxOutput(
                                    coin: new Coin(
                                        fromTxHash: new uint256(tx.TxId),
                                        fromOutputIndex: o.N,
                                        amount: amount,
                                        scriptPubKey: script),
                                    spentTxPoint: spentTxPoint));
                            }
                        }

                        return new Result<IEnumerable<ITxOutput>>(outputs);
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
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/tx/{txId}";

            return await HttpHelper.GetAsyncResult<ITxPoint>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(content);

                        var output = tx.Outputs[(int)outputNo];

                        var txOutput = !string.IsNullOrEmpty(output.SpentTxId) && output.SpentIndex != null
                            ? new TxPoint(output.SpentIndex.Value, output.SpentTxId)
                            : null;

                        return txOutput;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<string>> GetRawTxAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/rawtx/{txId}";

            return await HttpHelper.GetAsyncResult<string>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) => JsonConvert.DeserializeObject<RawTx>(content).TxInHex,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}