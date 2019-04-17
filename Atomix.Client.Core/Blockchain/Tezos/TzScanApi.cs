using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Blockchain.Tezos.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomix.Blockchain.Tezos
{
    public class TzScanApi : ITezosBlockchainApi
    {
        public const string Mainnet = "https://api6.tzscan.io/";
        public const string Alphanet = "https://api.alphanet.tzscan.io/";

        public const string MainnetRpc = "https://mainnet-node.tzscan.io";
        public const string AlphanetRpc = "http://alphanet-node.tzscan.io:80";
        public const string ZeronetRpc = "https://zeronet-node.tzscan.io:80";

        private const int HttpTooManyRequests = 429;

        internal class OperationHeader<T>
        {
            [JsonProperty("hash")]
            public string Hash { get; set; }
            [JsonProperty("block_hash")]
            public string BlockHash { get; set; }
            [JsonProperty("network_hash")]
            public string NetworkHash { get; set; }
            [JsonProperty("type")]
            public T Type { get; set; }
        }

        internal class Address
        {
            [JsonProperty("tz")]
            public string Tz { get; set; }
        }

        internal class Operation
        {
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("src")]
            public Address Source { get; set; }
            [JsonProperty("public_key")]
            public string PublicKey { get; set; }
            [JsonProperty("amount")]
            public string Amount { get; set; }
            [JsonProperty("destination")]
            public Address Destination { get; set; }
            [JsonProperty("str_parameters")]
            public string Parameters { get; set; }
            [JsonProperty("failed")]
            public bool Failed { get; set; }
            [JsonProperty("internal")]
            public bool Internal { get; set; }
            [JsonProperty("burn")]
            public long Burn { get; set; }
            [JsonProperty("counter")]
            public long Counter { get; set; }
            [JsonProperty("fee")]
            public long Fee { get; set; }
            [JsonProperty("gas_limit")]
            public string GasLimit { get; set; }
            [JsonProperty("storage_limit")]
            public string StorageLimit { get; set; }
            [JsonProperty("op_level")]
            public long OpLevel { get; set; }
            [JsonProperty("timestamp")]
            public DateTime TimeStamp { get; set; }
        }

        internal class OperationType<T>
        {
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("source")]
            public Address Source { get; set; }
            [JsonProperty("operations")]
            public List<T> Operations { get; set; }
        }

        private readonly string _rpcProvider;
        private readonly string _apiBaseUrl;

        public TzScanApi(TezosNetwork network)
        {
            switch (network)
            {
                case TezosNetwork.Mainnet:
                    _rpcProvider = MainnetRpc;
                    _apiBaseUrl = Mainnet;
                    break;
                case TezosNetwork.Alphanet:
                    _rpcProvider = AlphanetRpc;
                    _apiBaseUrl = Alphanet;
                    break;
                default:
                    throw new NotSupportedException("Network not supported");
            }
        }

        public async Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tx = (TezosTransaction) transaction;

            var rpc = new Rpc(_rpcProvider);

            var opResults = await rpc
                .PreApplyOperations(tx.Head, tx.Operations, tx.SignedMessage.EncodedSignature)
                .ConfigureAwait(false);

            if (!opResults.Any())
                return null;

            string txId = null;

            if (opResults.Any() && opResults.All(op => op.Succeeded))
            {
                var injectedOperation = await rpc
                    .InjectOperations(tx.SignedMessage.SignedBytes)
                    .ConfigureAwait(false);

                txId = injectedOperation.ToString();
            }

            if (txId == null)
                Log.Error("TxId is null");

            tx.Id = txId;

            return tx.Id;
        }

        public Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"v3/operation/{txId}";

            return SendRequest(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent =>
                {
                    var operationHeader = JsonConvert.DeserializeObject<OperationHeader<OperationType<Operation>>>(responseContent);

                    return TxFromOperation(operationHeader);
                },
                cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var transactions = new List<IBlockchainTransaction>();

            for (var p = 0; ; p++)
            {
                var txsOnPage = (await GetTransactionsAsync(address, p, cancellationToken)
                    .ConfigureAwait(false))
                    .ToList();

                if (!txsOnPage.Any())
                    break;

                transactions.AddRange(txsOnPage);
            }

            return transactions;
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string address,
            int page,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"v3/operations/{address}?type=Transaction&p={page}"; // TODO: use all types!!!

            var txs = await SendRequest<IEnumerable<IBlockchainTransaction>>(
                    requestUri: requestUri,
                    method: HttpMethod.Get,
                    content: null,
                    responseHandler: responseContent =>
                    {
                        var operations =
                            JsonConvert.DeserializeObject<List<OperationHeader<OperationType<Operation>>>>(responseContent);

                        return operations
                            .Select(operationHeader => TxFromOperation(operationHeader, address))
                            .Where(tx => tx != null)
                            .ToList();
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return txs ?? Enumerable.Empty<IBlockchainTransaction>();
        }

        private IBlockchainTransaction TxFromOperation(
            OperationHeader<OperationType<Operation>> operationHeader,
            string address = null)
        {
            var tx = operationHeader.Type.Operations.
                FirstOrDefault(o => o.Kind.Equals(OperationType.Transaction));

            if (tx == null)
                return null;

            return new TezosTransaction
            {
                Id = operationHeader.Hash,
                From = tx.Source.Tz,
                To = tx.Destination.Tz,
                Amount = decimal.Parse(tx.Amount),
                Fee = tx.Fee,
                GasLimit = decimal.Parse(tx.GasLimit),
                StorageLimit = decimal.Parse(tx.StorageLimit),
                Params = tx.Parameters != null
                    ? JObject.Parse(tx.Parameters)
                    : null,
                Type = tx.Destination.Tz.Equals(address)
                    ? TezosTransaction.InputTransaction
                    : TezosTransaction.OutputTransaction,
                IsInternal = tx.Internal,

                BlockInfo = new BlockInfo
                {
                    Fees = tx.Fee,
                    FirstSeen = tx.TimeStamp,
                    BlockTime = tx.TimeStamp,
                    BlockHeight = tx.OpLevel,
                    Confirmations = tx.Failed ? 0 : 1
                }
            };
        }

        private async Task<T> SendRequest<T>(
            string requestUri,
            HttpMethod method,
            HttpContent content,
            Func<string, T> responseHandler,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpResponseMessage response;

            Log.Debug("Send request: {@request}", requestUri);

            try
            {
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
                Log.Error(e, "Http request error");
                return default(T);
            }

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                Log.Verbose($"Raw response content: {responseContent}");

                return responseHandler(responseContent);
            }

            if ((int) response.StatusCode == HttpTooManyRequests)
            {
                Log.Warning("Too many requests");
            }
            else
            {
                Log.Warning("Invalud response code: {@code}", response.StatusCode);
            }

            return default(T);
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public static string RpcByNetwork(TezosNetwork network)
        {
            switch (network)
            {
                case TezosNetwork.Mainnet:
                    return MainnetRpc;
                case TezosNetwork.Alphanet:
                    return AlphanetRpc;
                default:
                    throw new NotSupportedException("Network not supported");
            }
        }
    }
}