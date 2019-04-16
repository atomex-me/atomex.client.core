using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Blockchain.Abstract;
using Atomix.Common;
using Nethereum.Signer;
using Newtonsoft.Json;
using Serilog;

namespace Atomix.Blockchain.Ethereum
{
    public class EtherScanApi : IEthereumBlockchainApi
    {
        public const string MainNet = "https://api.etherscan.io/";
        public const string Ropsten = "http://api-ropsten.etherscan.io/";

        private const string ApiKey = "2R1AIHZZE5NVSHRQUGAHU8EYNYYZ5B2Y37";
        private const int MinDelayBetweenRequestMs = 1000; // 500
        private const int TooManyRequestsDelayMs = 20000;
        private const int MaxDelayMs = 1000;
        private const int HttpTooManyRequests = 429;
        private static long _lastRequestTimeStampMs;

        public string BaseUrl { get; }
        public int RequestAttemptsCount { get; } = 5;

        internal class Response<T>
        {
            [JsonProperty(PropertyName = "status")]
            public string Status { get; set; }
            [JsonProperty(PropertyName = "message")]
            public string Message { get; set; }
            [JsonProperty(PropertyName = "result")]
            public T Result { get; set; }
        }

        internal class Transaction
        {
            [JsonProperty(PropertyName = "blockNumber")]
            public string BlockNumber { get; set; }
            [JsonProperty(PropertyName = "timeStamp")]
            public string TimeStamp { get; set; }
            [JsonProperty(PropertyName = "hash")]
            public string Hash { get; set; }
            [JsonProperty(PropertyName = "nonce")]
            public string Nonce { get; set; }
            [JsonProperty(PropertyName = "blockHash")]
            public string BlockHash { get; set; }
            [JsonProperty(PropertyName = "transactionIndex")]
            public string TransactionIndex { get; set; }
            [JsonProperty(PropertyName = "from")]
            public string From { get; set; }
            [JsonProperty(PropertyName = "to")]
            public string To { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "gas")]
            public string Gas { get; set; }
            [JsonProperty(PropertyName = "gasPrice")]
            public string GasPrice { get; set; }
            [JsonProperty(PropertyName = "inError")]
            public string InError { get; set; }
            [JsonProperty(PropertyName = "txreceipt_status")]
            public string ReceiptStatus { get; set; }
            [JsonProperty(PropertyName = "input")]
            public string Input { get; set; }
            [JsonProperty(PropertyName = "contractAddress")]
            public string ContractAddress { get; set; }
            [JsonProperty(PropertyName = "cumulativeGasUsed")]
            public string CumulativeGasUsed { get; set; }
            [JsonProperty(PropertyName = "gasUsed")]
            public string GasUsed { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public string Confirmations { get; set; }
        }

        public EtherScanApi(Chain chain)
        {
            switch (chain)
            {
                case Chain.MainNet:
                    BaseUrl = MainNet;
                    break;
                case Chain.Ropsten:
                    BaseUrl = Ropsten;
                    break;
                default:
                    throw new NotSupportedException($"Chain {chain} not supported");
            }
        }

        public Task<BigInteger> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // TODO: add pagination support
            var requestUri = $"api?module=account&action=txlist&address={address}&sort=asc&apikey={ApiKey}";

            var transactions = await SendRequest(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent => ParseTransactions(responseContent, address, isInternal: false),
                cancellationToken: cancellationToken);

            requestUri = $"api?module=account&action=txlistinternal&address={address}&sort=asc&apikey={ApiKey}";

            var internalTransactions = await SendRequest(
                requestUri: requestUri,
                method: HttpMethod.Get,
                content: null,
                responseHandler: responseContent => ParseTransactions(responseContent, address, isInternal: true),
                cancellationToken: cancellationToken);

            return transactions.Concat(internalTransactions);
        }

        private IEnumerable<IBlockchainTransaction> ParseTransactions(
            string responseContent,
            string address,
            bool isInternal)
        {
            var txs = JsonConvert.DeserializeObject<Response<List<Transaction>>>(responseContent);

            return txs.Result.Select(t => new EthereumTransaction
            {
                Id = t.Hash,
                From = t.From.ToLowerInvariant(),
                To = t.To.ToLowerInvariant(),
                Input = t.Input,
                Amount = BigInteger.Parse(t.Value),
                Nonce = t.Nonce != null ?  BigInteger.Parse(t.Nonce) : 0,
                GasPrice = t.GasPrice != null ? BigInteger.Parse(t.GasPrice) : 0,
                GasLimit = BigInteger.Parse(t.Gas),
                GasUsed = BigInteger.Parse(t.GasUsed),
                Type = t.To.Equals(address)
                    ? EthereumTransaction.InputTransaction
                    : EthereumTransaction.OutputTransaction,
                ReceiptStatus = t.ReceiptStatus != null ? t.ReceiptStatus.Equals("1") : true,
                IsInternal = isInternal,

                BlockInfo = new BlockInfo
                {
                    BlockHeight = long.Parse(t.BlockNumber),
                    BlockTime = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(t.TimeStamp)),
                    Confirmations = t.Confirmations != null ? int.Parse(t.Confirmations) : 1,
                    Fees = long.Parse(t.GasUsed),
                    FirstSeen = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(t.TimeStamp))
                }
            });
        }

        public Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
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
                    Log.Error(e, "Http request error");

                    if (attempts < RequestAttemptsCount)
                        continue;

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

                if ((int)response.StatusCode == HttpTooManyRequests)
                {
                    Log.Debug("Too many requests");

                    for (var i = 0; i < TooManyRequestsDelayMs / MaxDelayMs; ++i)
                        await Task.Delay(MaxDelayMs, cancellationToken);

                    continue;
                }

                tryToSend = false;
            }

            Log.Warning("Invalid response code: {@code}", response.StatusCode);

            return default(T);
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static readonly object LimitControlSync = new object();

        private static async Task RequestLimitControl(
            CancellationToken cancellationToken)
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