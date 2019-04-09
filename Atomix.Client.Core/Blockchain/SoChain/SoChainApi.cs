using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.BitcoinBased;
using Atomix.Common;
using NBitcoin;
using Newtonsoft.Json;
using Serilog;

namespace Atomix.Blockchain.SoChain
{
    public class SoChainApi : IInOutBlockchainApi
    {
        internal class SendTx
        {
            [JsonProperty(PropertyName = "tx_hex")]
            public string TxHex { get; }

            public SendTx(string txHex) {
                TxHex = txHex;
            }
        }

        internal class SendTxId
        {
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
        }

        internal class Output
        {
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }
            [JsonProperty(PropertyName = "script")]
            public string Script { get; set; }
        }

        internal class OutputRef
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
        }

        internal class Input
        {
            [JsonProperty(PropertyName = "from_output")]
            public OutputRef FromOutput { get; set; }
            [JsonProperty(PropertyName = "input_no")]
            public uint InputNo { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }
            [JsonProperty(PropertyName = "script")]
            public string Script { get; set; }
            [JsonProperty(PropertyName = "witness")]
            public List<string> Witness { get; set; }
        }

        internal class InputRef
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "input_no")]
            public uint InputNo { get; set; }
        }

        internal class Tx
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "blockhash")]
            public string BlockHash { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public List<Input> Inputs { get; set; }
            [JsonProperty(PropertyName = "outputs")]
            public List<Output> Outputs { get; set; }
            [JsonProperty(PropertyName = "tx_hex")]
            public string TxHex { get; set; }
            [JsonProperty(PropertyName = "block_no")]
            public int? BlockNo { get; set; }
            [JsonProperty(PropertyName = "sent_value")]
            public string SentValue { get; set; }
            [JsonProperty(PropertyName = "fee")]
            public string Fee { get; set; }
            //[JsonProperty(PropertyName = "size")]
            //public int Size { get; set; }
            //[JsonProperty(PropertyName = "version")]
            //public int Version { get; set; }
            //[JsonProperty(PropertyName = "locktime")]
            //public int LockTime { get; set; }
        }

        internal class TxInputs
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public List<Input> Inputs { get; set; }
        }

        internal class TxSingleInput
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public Input Inputs { get; set; }
        }

        internal class TxOutput
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
            [JsonProperty(PropertyName = "input_no")]
            public int InputNo { get; set; }
            [JsonProperty(PropertyName = "script_asm")]
            public string ScriptAsm { get; set; }
            [JsonProperty(PropertyName = "script_hex")]
            public string ScriptHex { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
        }

        internal class AddressOutputs
        {
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "txs")]
            public List<TxOutput> Txs { get; set; }
        }

        internal class InputDisplayData
        {
            [JsonProperty(PropertyName = "input_no")]
            public uint InputNo { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "received_from")]
            public OutputRef ReceivedFrom { get; set; }
        }

        internal class Incoming
        {
            [JsonProperty(PropertyName = "output_no")]
            public uint OutputNo { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "spent")]
            public InputRef Spent { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public List<InputDisplayData> Inputs { get; set; }
            [JsonProperty(PropertyName = "req_sigs")]
            public int ReqSigs { get; set; }
            [JsonProperty(PropertyName = "script_asm")]
            public string ScriptAsm { get; set; }
            [JsonProperty(PropertyName = "script_hex")]
            public string ScriptHex { get; set; }
        }

        internal class OutputDisplayData
        {
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "spent")]
            public InputRef Spent { get; set; }
        }

        internal class Outgoing
        {
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "outputs")]
            public List<OutputDisplayData> Outputs { get; set; }
        }

        internal class TxDisplayData
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "block_no")]
            public int? BlockNo { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
            [JsonProperty(PropertyName = "incoming")]
            public Incoming Incoming { get; set; }
            [JsonProperty(PropertyName = "outgoing")]
            public Outgoing Outgoing { get; set; }
        }

        internal class AddressDisplayData
        {
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "balance")]
            public string Balance { get; set; }
            [JsonProperty(PropertyName = "txs")]
            public List<TxDisplayData> Txs { get; set; }
        }

        internal class TxConfirmationInfo
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "is_confirmed")]
            public bool IsConfirmed { get; set; }
        }

        internal class TxOutputSpentInfo
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "output_no")]
            public uint OutputNo { get; set; }
            [JsonProperty(PropertyName = "is_spent")]
            public bool IsSpent { get; set; }
            [JsonProperty(PropertyName = "spent")]
            public InputRef Spent { get; set; }
        }

        internal class ConfidenceInfo
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "confidence")]
            public decimal Confidence { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
        }

        internal class Response<T>
        {
            [JsonProperty(PropertyName = "status")]
            public string Status { get; set; }
            [JsonProperty(PropertyName = "data")]
            public T Data { get; set; }
        }

        private static Dictionary<string, string> Acronyms { get; } = new Dictionary<string, string>
        {
            { "Main", "BTC" },
            { "TestNet", "BTCTEST" },
            { "ltc-main", "LTC" },
            { "ltc-test", "LTCTEST" },
            { "dash-main", "DASH" },
            { "dash-test", "DASHTEST" },
            { "doge-main", "DOGE" },
            { "doge-test", "DOGETEST" },
            //{ "zec-main", "ZEC" },
            //{ "zec-test", "ZECTEST" }
        };

        private const int Satoshi = 100000000;
        private const int MinDelayBetweenRequestMs = 1000; // 500
        private const int TooManyRequestsDelayMs = 20000;
        private const int MaxDelayMs = 1000;
        private const int HttpTooManyRequests = 429;
        private static long _lastRequestTimeStampMs;

        public BitcoinBasedCurrency Currency { get; }
        public string NetworkAcronym { get; }
        public string BaseUrl { get; } = "https://chain.so/";
        public int RequestAttemptsCount { get; } = 5;
        
        public SoChainApi(BitcoinBasedCurrency currency)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));

            var networkAcronym = AcronymByNetwork(currency.Network);

            if (!Acronyms.ContainsValue(networkAcronym))
                throw new NotSupportedException($"Network {networkAcronym} not supported by chain.so api!");

            NetworkAcronym = networkAcronym;
        }

        public async Task<long> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var unspentOuts = await GetUnspentOutputsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return unspentOuts.Sum(o => o.Value);
        }

        public Task<ITxPoint> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/get_tx_inputs/{NetworkAcronym}/{txId}/{inputNo}";

            return SendRequest<ITxPoint>(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var response = JsonConvert.DeserializeObject<Response<TxSingleInput>>(responseContent);
                    var input = response.Data.Inputs;
    
                    var witScript = WitScript.Empty;

                    if (input.Witness != null)
                        witScript = input.Witness.Aggregate(witScript, (current, witness) => current + new WitScript(witness));
                    
                    return new BitcoinBasedTxPoint(new IndexedTxIn
                    {
                        TxIn = new TxIn(new OutPoint(new uint256(input.FromOutput.TxId), input.FromOutput.OutputNo), new Script(input.Script)),
                        Index = input.InputNo,
                        WitScript = witScript,
                    });
                }, 
                cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<ITxPoint>> GetInputsAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/get_tx_inputs/{NetworkAcronym}/{txId}";

            return SendRequest<IEnumerable<ITxPoint>>(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var outputs = JsonConvert.DeserializeObject<Response<TxInputs>>(responseContent);

                    return outputs.Data.Inputs
                        .Select(i => 
                        {
                            var witScript = WitScript.Empty;

                            if (i.Witness != null)
                                witScript = i.Witness.Aggregate(witScript, (current, witness) => current + new WitScript(witness));

                            return new BitcoinBasedTxPoint(new IndexedTxIn
                            {
                                TxIn = new TxIn(new OutPoint(new uint256(i.FromOutput.TxId), i.FromOutput.OutputNo), new Script(i.Script)),
                                Index = i.InputNo,
                                WitScript = witScript,
                            });
                        });

                },
                cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var addParams = afterTxId != null ? $"{address}/{afterTxId}" : $"{address}";
            var requestUri = $"api/v2/get_tx_unspent/{NetworkAcronym}/{addParams}";

            return SendRequest<IEnumerable<ITxOutput>>(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var outputs = JsonConvert.DeserializeObject<Response<AddressOutputs>>(responseContent);

                    return outputs.Data.Txs
                        .Select(u =>
                            new BitcoinBasedTxOutput(
                                coin: new Coin(
                                    fromTxHash: new uint256(u.TxId),
                                    fromOutputIndex: (uint) u.OutputNo,
                                    amount: new Money(decimal.Parse(u.Value, CultureInfo.InvariantCulture),
                                        MoneyUnit.BTC),
                                    scriptPubKey: new Script(Hex.FromString(u.ScriptHex))),
                                spentTxPoint: null));

                },
                cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<ITxOutput>> GetReceivedOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var addParams = afterTxId != null ? $"{address}/{afterTxId}" : $"{address}";
            var requestUri = $"api/v2/get_tx_received/{NetworkAcronym}/{addParams}";

            return SendRequest<IEnumerable<ITxOutput>>(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var outputs = JsonConvert.DeserializeObject<Response<AddressOutputs>>(responseContent);

                    return outputs.Data.Txs
                        .Select(u =>
                            new BitcoinBasedTxOutput(
                                coin: new Coin(
                                    fromTxHash: new uint256(u.TxId),
                                    fromOutputIndex: (uint)u.OutputNo,
                                    amount: new Money(decimal.Parse(u.Value, CultureInfo.InvariantCulture),
                                        MoneyUnit.BTC),
                                    scriptPubKey: new Script(Hex.FromString(u.ScriptHex))),
                                spentTxPoint: null));

                },
                cancellationToken: cancellationToken);
        }

        public Task<IEnumerable<ITxOutput>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/address/{NetworkAcronym}/{address}";

            return SendRequest<IEnumerable<ITxOutput>>(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContext =>
                {
                    var displayData = JsonConvert.DeserializeObject<Response<AddressDisplayData>>(responseContext);

                    var outputs = new List<ITxOutput>();

                    foreach (var tx in displayData.Data.Txs)
                    {
                        if (tx.Incoming == null)
                            continue;

                        var spentTxPoint = tx.Incoming.Spent != null
                            ? new TxPoint(tx.Incoming.Spent.InputNo, tx.Incoming.Spent.TxId)
                            : null;

                        var amount = new Money(decimal.Parse(tx.Incoming.Value, CultureInfo.InvariantCulture),
                            MoneyUnit.BTC);

                        var script = new Script(Hex.FromString(tx.Incoming.ScriptHex));

                        outputs.Add(new BitcoinBasedTxOutput(
                            coin: new Coin(
                                fromTxHash: new uint256(tx.TxId),
                                fromOutputIndex: tx.Incoming.OutputNo,
                                amount: amount,
                                scriptPubKey: script),
                            spentTxPoint: spentTxPoint));
                    }

                    return outputs;

                },
                cancellationToken: cancellationToken);
        }

        public Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/tx/{NetworkAcronym}/{txId}";

            return SendRequest<IBlockchainTransaction>(
                requestUri: requestUri,
                method: HttpMethod.Get, 
                content: null,
                responseHandler: responseContent =>
                {
                    var tx = JsonConvert.DeserializeObject<Response<Tx>>(responseContent);

                    var fees = (long) (decimal.Parse(tx.Data.Fee, CultureInfo.InvariantCulture) * Satoshi);

                    return new BitcoinBasedTransaction(
                        currency: Currency,
                        tx: Transaction.Parse(tx.Data.TxHex, Currency.Network),
                        blockInfo: new BlockInfo
                        {
                            Fees = fees,
                            Confirmations = tx.Data.Confirmations,
                            BlockHeight = tx.Data.BlockNo.GetValueOrDefault(0),
                            FirstSeen = tx.Data.Time.ToUtcDateTime(),
                            BlockTime = tx.Data.Time.ToUtcDateTime()
                        }
                    );
                },
                cancellationToken: cancellationToken);
        }

        public Task<bool> IsTransactionConfirmed(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/is_tx_confirmed/{NetworkAcronym}/{txId}";

            return SendRequest(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var info = JsonConvert.DeserializeObject<Response<TxConfirmationInfo>>(responseContent);
                    return info.Data.IsConfirmed;

                },
                cancellationToken: cancellationToken);
        }

        public Task<ITxPoint> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/is_tx_spent/{NetworkAcronym}/{txId}/{outputNo}";

            return SendRequest<ITxPoint>(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content:null,
                responseHandler: responseContent =>
                {
                    var info = JsonConvert.DeserializeObject<Response<TxOutputSpentInfo>>(responseContent);

                    return info.Data.IsSpent
                        ? new TxPoint(info.Data.Spent.InputNo, info.Data.Spent.TxId)
                        : null;

                },
                cancellationToken: cancellationToken);
        }

        public Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = (IBitcoinBasedTransaction) transaction;

            var requestUri = $"api/v2/send_tx/{NetworkAcronym}";

            var content = new StringContent(
                content: JsonConvert.SerializeObject(new SendTx(tx.ToBytes().ToHexString())),
                encoding: Encoding.UTF8,
                mediaType: "application/json");

            return SendRequest(
                requestUri: requestUri,
                method: HttpMethod.Post,
                content: content, 
                responseHandler: responseContent => JsonConvert.DeserializeObject<Response<SendTxId>>(responseContent).Data.TxId,
                cancellationToken: cancellationToken);
        }


        public Task<ConfidenceInformation> GetConfidenceAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api/v2/get_confidence/{NetworkAcronym}/{txId}";

            return SendRequest(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var info = JsonConvert.DeserializeObject<Response<ConfidenceInfo>>(responseContent);

                    return new ConfidenceInformation
                    {
                        TxId = info.Data.TxId,
                        Confidence = info.Data.Confidence,
                        Confirmations = info.Data.Confirmations
                    };
                },
                cancellationToken: cancellationToken);
        }

        private async Task<T> SendRequest<T>(
            string requestUri,
            HttpMethod method,
            HttpContent content,
            Func<string, T> responseHandler,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpResponseMessage response = null;
            var tryToSend = true;
            var attempts = 0;

            while (tryToSend)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                await RequestLimitControl(cancellationToken)
                    .ConfigureAwait(false);

                Log.Debug("Send request: {@request}", requestUri);

                try
                {
                    attempts++;

                    if (method == HttpMethod.Get)
                    {
                        response = await CreateHttpClient()
                            .GetAsync(requestUri, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (method == HttpMethod.Post)
                    {
                        response = await CreateHttpClient()
                            .PostAsync(requestUri, content, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        throw new ArgumentException("Http method not supported");
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Http GET requestUri error");

                    if (attempts < RequestAttemptsCount)
                        continue;

                    throw;
                }

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content
                        .ReadAsStringAsync()
                        .ConfigureAwait(false);

                    Log.Verbose($"Raw response content: {responseContent}");

                    return responseHandler(responseContent);
                }

                if ((int)response.StatusCode == HttpTooManyRequests)
                {
                    Log.Debug("Too many requests");

                    for (var i = 0; i < TooManyRequestsDelayMs / MaxDelayMs; ++i)
                        await Task.Delay(MaxDelayMs, cancellationToken);

                    continue;
                }

                tryToSend = false;
            }

            throw new Exception($"Invalid response code: {response.StatusCode}");
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static string AcronymByNetwork(Network network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            if (Acronyms.TryGetValue(network.Name, out var acronym))
                return acronym;

            throw new NotSupportedException($"Network {network.Name} not supported!");
        }

        private static readonly object LimitControlSync = new object();

        private static async Task RequestLimitControl(CancellationToken cancellationToken)
        {
            var isCompleted = false;

            while (!isCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                Monitor.Enter(LimitControlSync);

                var timeStampMs = (long)DateTime.Now.ToUnixTimeMs();
                var differenceMs = timeStampMs - _lastRequestTimeStampMs;

                if (differenceMs < MinDelayBetweenRequestMs)
                {
                    Monitor.Exit(LimitControlSync);

                    await Task.Delay((int)(MinDelayBetweenRequestMs - differenceMs), cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    _lastRequestTimeStampMs = timeStampMs;
                    Monitor.Exit(LimitControlSync);

                    isCompleted = true;
                }
            }
        }
    }
}