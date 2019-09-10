using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using Newtonsoft.Json;

namespace Atomix.Blockchain.Insight
{
    public class InsightApi : IInOutBlockchainApi
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

        public const string InsightLiteCoreMainNet = "https://insight.litecore.io";
        public const string InsightLiteCoreTestNet = "https://testnet.litecore.io";

        private const int MinDelayBetweenRequestMs = 500; // 500
        private static readonly RequestLimitChecker RequestLimitChecker = new RequestLimitChecker(MinDelayBetweenRequestMs);
        private string BaseUri { get; }

        public BitcoinBasedCurrency Currency { get; }
        public int MaxRequestAttemptsCount { get; } = 1;

        public InsightApi(BitcoinBasedCurrency currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = configuration["InsightBaseUri"].ToString();
        }

        public InsightApi(BitcoinBasedCurrency currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri)); ;
        }

        public async Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/addr/{address}/balance";

            return await HttpHelper.GetAsync(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContent =>
                    {
                        return long.Parse(responseContent, CultureInfo.InvariantCulture) / (decimal)Currency.DigitsMultiplier;
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<ITxPoint> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/tx/{txId}";

            return await HttpHelper.GetAsync<ITxPoint>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContent =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(responseContent);

                        var txIn = tx.Inputs[(int)inputNo];

                        return new BitcoinBasedTxPoint(new IndexedTxIn
                        {
                            TxIn = new TxIn(new OutPoint(new uint256(txIn.TxId), txIn.VOut), new Script(Hex.FromString(txIn.ScriptSig.Hex))),
                            Index = txIn.N,
                            //WitScript = null,
                        });
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/addr/{address}/utxo";

            return await HttpHelper.GetAsync<IEnumerable<ITxOutput>>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContent =>
                    {
                        var utxoList = JsonConvert.DeserializeObject<List<Utxo>>(responseContent);

                        return utxoList.Select(u => new BitcoinBasedTxOutput(
                            coin: new Coin(
                                fromTxHash: new uint256(u.TxId),
                                fromOutputIndex: u.VOut,
                                amount: new Money(u.Satoshis, MoneyUnit.Satoshi),
                                scriptPubKey: new Script(Hex.FromString(u.ScriptPubKey))),
                            spentTxPoint: null));
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? Enumerable.Empty<ITxOutput>();
        }

        public async Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/txs/?address={address}"; // todo: pagination?

            return await HttpHelper.GetAsync<IEnumerable<ITxOutput>>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContext =>
                    {
                        var txs = JsonConvert.DeserializeObject<Txs>(responseContext);

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

                                var spentTxPoint = !string.IsNullOrEmpty(o.SpentTxId)
                                    ? new TxPoint(o.SpentIndex.Value, o.SpentTxId)
                                    : null;

                                var amountDecimal = decimal.Parse(o.Value, CultureInfo.InvariantCulture);
                                var amount = new Money(amountDecimal, MoneyUnit.BTC);

                                var script = new Script(Hex.FromString(o.ScriptPubKey.Hex));

                                outputs.Add(new BitcoinBasedTxOutput(
                                    coin: new Coin(
                                        fromTxHash: new uint256(tx.TxId),
                                        fromOutputIndex: o.N,
                                        amount: amount,
                                        scriptPubKey: script),
                                    spentTxPoint: spentTxPoint));
                            }
                        }

                        return outputs;
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? Enumerable.Empty<ITxOutput>();
        }

        public async Task<ITxPoint> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/tx/{txId}";

            return await HttpHelper.GetAsync<ITxPoint>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContent =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(responseContent);

                        var output = tx.Outputs[(int)outputNo];

                        return !string.IsNullOrEmpty(output.SpentTxId)
                            ? new TxPoint(output.SpentIndex.Value, output.SpentTxId)
                            : null;
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var rawTxHex = await GetRawTx(txId, cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"api/tx/{txId}";

            return await HttpHelper.GetAsync<IBlockchainTransaction>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContent =>
                    {
                        var tx = JsonConvert.DeserializeObject<Tx>(responseContent);

                        var fees = tx.Fees;

                        return new BitcoinBasedTransaction(
                            currency: Currency,
                            tx: Transaction.Parse(rawTxHex, Currency.Network),
                            blockInfo: new BlockInfo
                            {
                                Fees = (long)(fees * Currency.DigitsMultiplier),
                                Confirmations = tx.Confirmations,
                                BlockHeight = tx.BlockHeight.GetValueOrDefault(0),
                                FirstSeen = tx.Time.ToUtcDateTime(),
                                BlockTime = tx.Time.ToUtcDateTime()
                            }
                        );
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsByIdAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = await GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            return tx != null
                ? new[] { tx }
                : Enumerable.Empty<IBlockchainTransaction>();
        }

        public async Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = (IBitcoinBasedTransaction)transaction;
            var txHex = tx.ToBytes().ToHexString();

            var requestUri = $"api/tx/send"; //?rawtx={txHex}";

            var parameters = new Dictionary<string, string> {{"rawtx", txHex}};

            var content = new FormUrlEncodedContent(parameters);

            return await HttpHelper.PostAsync(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    content: content,
                    responseHandler: responseContent => JsonConvert.DeserializeObject<SendTxId>(responseContent).TxId,
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<string> GetRawTx(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/rawtx/{txId}";

            return await HttpHelper.GetAsync(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: responseContent =>
                    {
                        return JsonConvert.DeserializeObject<RawTx>(responseContent).TxInHex;
                    },
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}