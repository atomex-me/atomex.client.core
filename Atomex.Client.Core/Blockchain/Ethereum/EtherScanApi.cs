using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core.Entities;
using Nethereum.Signer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Blockchain.Ethereum
{
    public class EtherScanApi : IEthereumBlockchainApi
    {
        private const string MainNet = "https://api.etherscan.io/";
        private const string Ropsten = "http://api-ropsten.etherscan.io/";

        private const string ApiKey = "2R1AIHZZE5NVSHRQUGAHU8EYNYYZ5B2Y37";
        private const int MinDelayBetweenRequestMs = 1000; // 500

        private static readonly RequestLimitChecker RequestLimitChecker 
            = new RequestLimitChecker(MinDelayBetweenRequestMs);

        private string BaseUrl { get; }
        private int MaxRequestAttemptsCount { get; } = 1;

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

        private Currency Currency { get; }

        public EtherScanApi(Currency currency, Chain chain)
        {
            Currency = currency;

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

        public async Task<decimal> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api?module=account&action=balance&address={address}&apikey={ApiKey}";

            return await HttpHelper.GetAsync(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: responseContent =>
                   {
                       var json = JsonConvert.DeserializeObject<JObject>(responseContent);

                       return json.ContainsKey("result")
                           ? Atomex.Ethereum.WeiToEth(new BigInteger(long.Parse(json["result"].ToString())))
                           : 0;
                   },
                   requestLimitChecker: RequestLimitChecker,
                   maxAttempts: MaxRequestAttemptsCount,
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public Task<BigInteger> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<IBlockchainTransaction> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<IBlockchainTransaction>> GetTransactionsByIdAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetInternalTransactionsAsync(
            string txId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var requestUri = $"api?module=account&action=txlistinternal&txhash={txId}&apikey={ApiKey}";

            return await HttpHelper.GetAsync(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: responseContent => ParseTransactions(responseContent, txId: txId, isInternal: true),
                   requestLimitChecker: RequestLimitChecker,
                   maxAttempts: MaxRequestAttemptsCount,
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false) ?? Enumerable.Empty<IBlockchainTransaction>();
        }

        public async Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // TODO: add pagination support
            var requestUri = $"api?module=account&action=txlist&address={address}&sort=asc&apikey={ApiKey}";

            var transactions = await HttpHelper.GetAsync(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: responseContent => ParseTransactions(responseContent, txId: null, isInternal: false),
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? Enumerable.Empty<IBlockchainTransaction>();

            requestUri = $"api?module=account&action=txlistinternal&address={address}&sort=asc&apikey={ApiKey}";

            var internalTransactions = await HttpHelper.GetAsync(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: responseContent => ParseTransactions(responseContent, txId: null, isInternal: true),
                    requestLimitChecker: RequestLimitChecker,
                    maxAttempts: MaxRequestAttemptsCount,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? Enumerable.Empty<IBlockchainTransaction>();

            return transactions.Concat(internalTransactions);
        }

        public Task<string> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        private IEnumerable<IBlockchainTransaction> ParseTransactions(
            string responseContent,
            string txId,
            bool isInternal)
        {
            var result = new List<IBlockchainTransaction>();

            var txs = JsonConvert.DeserializeObject<Response<List<Transaction>>>(responseContent);

            var internalCounters = new Dictionary<string, int>();

            foreach (var tx in txs.Result)
            {
                if (txId == null && tx.Hash == null)
                {
                    Log.Warning("Tx with null hash received");
                    continue;
                }

                var id = tx.Hash ?? txId;

                var internalIndex = 0;

                if (isInternal)
                {
                    if (internalCounters.TryGetValue(id, out var index))
                    {
                        internalIndex = ++index;
                        internalCounters[id] = internalIndex;
                    }
                    else
                    {
                        internalCounters.Add(id, internalIndex);
                    }
                }

                result.Add(new EthereumTransaction(Currency)
                {
                    Id = id,
                    From = tx.From.ToLowerInvariant(),
                    To = tx.To.ToLowerInvariant(),
                    Input = tx.Input,
                    Amount = BigInteger.Parse(tx.Value),
                    Nonce = tx.Nonce != null ? BigInteger.Parse(tx.Nonce) : 0,
                    GasPrice = tx.GasPrice != null ? BigInteger.Parse(tx.GasPrice) : 0,
                    GasLimit = BigInteger.Parse(tx.Gas),
                    GasUsed = BigInteger.Parse(tx.GasUsed),
                    Type = EthereumTransaction.UnknownTransaction,
                    ReceiptStatus = tx.ReceiptStatus?.Equals("1") ?? true,
                    IsInternal = isInternal,
                    InternalIndex = internalIndex,

                    BlockInfo = new BlockInfo
                    {
                        BlockHeight = long.Parse(tx.BlockNumber),
                        BlockTime = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),
                        Confirmations = tx.Confirmations != null ? int.Parse(tx.Confirmations) : 1,
                        Fees = long.Parse(tx.GasUsed),
                        FirstSeen = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp))
                    }
                });
            }

            return result;
        }
    }
}