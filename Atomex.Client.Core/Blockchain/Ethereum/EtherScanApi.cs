using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Blockchain.Ethereum
{
    public class EtherScanApi : IEthereumBlockchainApi
    {
        private const string ApiKey = "2R1AIHZZE5NVSHRQUGAHU8EYNYYZ5B2Y37";
        private const int MinDelayBetweenRequestMs = 1000;

        private static readonly RequestLimitControl RequestLimitControl 
            = new RequestLimitControl(MinDelayBetweenRequestMs);

        private string BaseUrl { get; }

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

        public class ContractEvent
        {
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }

            [JsonProperty(PropertyName = "topics")]
            public List<string> Topics { get; set; }

            [JsonProperty(PropertyName = "data")]
            public string HexData { get; set; }

            [JsonProperty(PropertyName = "blockNumber")]
            public string HexBlockNumber { get; set; }

            [JsonProperty(PropertyName = "timeStamp")]
            public string HexTimeStamp { get; set; }

            [JsonProperty(PropertyName = "gasPrice")]
            public string HexGasPrice { get; set; }

            [JsonProperty(PropertyName = "gasUsed")]
            public string HexGasUsed { get; set; }

            [JsonProperty(PropertyName = "logIndex")]
            public string HexLogIndex { get; set; }

            [JsonProperty(PropertyName = "transactionHash")]
            public string HexTransactionHash { get; set; }

            [JsonProperty(PropertyName = "transactionIndex")]
            public string HexTransactionIndex { get; set; }

            public string EventSignatureHash()
            {
                if (Topics != null && Topics.Count > 0)
                    return Topics[0];

                throw new Exception("Contract event does not contain event signature hash");
            }
        }

        private Currency Currency { get; }

        public EtherScanApi(Atomex.Ethereum currency)
        {
            Currency = currency;
            BaseUrl = currency.BlockchainApiBaseUri;
        }

        public async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api?module=account&action=balance&address={address}&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<decimal>(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: (response, content) =>
                   {
                       var json = JsonConvert.DeserializeObject<JObject>(content);

                       return json.ContainsKey("result")
                           ? Atomex.Ethereum.WeiToEth(new BigInteger(long.Parse(json["result"].ToString())))
                           : 0;
                   },
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetInternalTransactionsAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api?module=account&action=txlistinternal&txhash={txId}&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: (response, content) => new Result<IEnumerable<IBlockchainTransaction>>(
                       ParseTransactions(content, txId: txId, isInternal: true)),
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            // todo: add pagination support
            var result = await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=account&action=txlist&address={address}&sort=asc&apikey={ApiKey}",
                    responseHandler: (response, content) => new Result<IEnumerable<IBlockchainTransaction>>(
                        ParseTransactions(content, txId: null, isInternal: false)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.HasError)
                return result;

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var internalResult = await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=account&action=txlistinternal&address={address}&sort=asc&apikey={ApiKey}",
                    responseHandler: (response, content) => new Result<IEnumerable<IBlockchainTransaction>>(
                        ParseTransactions(content, txId: null, isInternal: true)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (internalResult.HasError)
                return internalResult;

            var txs = result.Value.Concat(internalResult.Value);

            return new Result<IEnumerable<IBlockchainTransaction>>(txs);
        }

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            CancellationToken cancellationToken = default)
        {
            return GetContractEventsAsync(address, fromBlock, toBlock, cancellationToken, topic0);
        }

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            string topic1,
            CancellationToken cancellationToken = default)
        {
            return GetContractEventsAsync(address, fromBlock, toBlock, cancellationToken, topic0, topic1);
        }

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            string topic1,
            string topic2,
            CancellationToken cancellationToken = default)
        {
            return GetContractEventsAsync(address, fromBlock, toBlock, cancellationToken, topic0, topic1, topic2);
        }

        public async Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock = ulong.MinValue,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default,
            params string[] topics)
        {
            var fromBlockStr = BlockNumberToStr(fromBlock);
            var toBlockStr = BlockNumberToStr(toBlock);
            var topicsStr = TopicsToStr(topics);

            var uri = $"api?module=logs&action=getLogs&address={address}&fromBlock={fromBlockStr}&toBlock={toBlockStr}{topicsStr}&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: uri,
                    responseHandler: (response, content) => new Result<IEnumerable<ContractEvent>>(
                        JsonConvert.DeserializeObject<Response<List<ContractEvent>>>(content).Result),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static string BlockNumberToStr(ulong blockNumber)
        {
            if (blockNumber == ulong.MaxValue)
                return "latest";

            // "earlest" and "pending" not supported by EtherScan yet

            return blockNumber.ToString();
        }

        private static string TopicsToStr(params string[] topics)
        {
            var result = string.Empty;

            if (topics == null)
                return result;

            var lastTopic = -1;

            for (var i = 0; i < topics.Length; ++i)
            {
                if (topics[i] == null)
                    continue;

                if (lastTopic != -1)                       
                    result += $"&topic{lastTopic}_{i}_opr=and";  

                result += $"&topic{i}={topics[i]}";

                lastTopic = i;
            }

            return result;
        }

        public Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
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

                var state = tx.ReceiptStatus != null
                    ? tx.ReceiptStatus.Equals("1")
                        ? BlockchainTransactionState.Confirmed
                        : BlockchainTransactionState.Failed
                    : isInternal
                        ? BlockchainTransactionState.Confirmed
                        : BlockchainTransactionState.Unconfirmed;

                result.Add(new EthereumTransaction
                {
                    Id = id,
                    Currency = Currency,
                    Type = BlockchainTransactionType.Unknown,
                    State = state,
                    CreationTime = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),

                    From = tx.From.ToLowerInvariant(),
                    To = tx.To.ToLowerInvariant(),
                    Input = tx.Input,
                    Amount = BigInteger.Parse(tx.Value),
                    Nonce = tx.Nonce != null
                        ? BigInteger.Parse(tx.Nonce)
                        : 0,
                    GasPrice = tx.GasPrice != null
                        ? BigInteger.Parse(tx.GasPrice)
                        : 0,
                    GasLimit = BigInteger.Parse(tx.Gas),
                    GasUsed = BigInteger.Parse(tx.GasUsed), 
                    ReceiptStatus = state == BlockchainTransactionState.Confirmed,
                    IsInternal = isInternal,
                    InternalIndex = internalIndex,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = tx.Confirmations != null
                            ? int.Parse(tx.Confirmations)
                            : 1,
                        BlockHash = tx.BlockHash,
                        BlockHeight = long.Parse(tx.BlockNumber),
                        BlockTime = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),
                        FirstSeen = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp))
                    }
                });
            }

            return result;
        }
    }
}