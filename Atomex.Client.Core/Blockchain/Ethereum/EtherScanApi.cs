using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Blockchain.Ethereum.ERC20;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.Ethereum
{
    public class EtherScanApi : BlockchainApi, IEthereumBlockchainApi, IGasPriceProvider
    {
        private const string ApiKey = "2R1AIHZZE5NVSHRQUGAHU8EYNYYZ5B2Y37";
        private const int MinDelayBetweenRequestMs = 1000;
        private static readonly int prefixOffset = 2;

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
            [JsonProperty(PropertyName = "isError")]
            public string IsError { get; set; }
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

        public override async Task<Result<decimal>> GetBalanceAsync(
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
                           ? Atomex.Ethereum.WeiToEth(BigInteger.Parse(json["result"].ToString()))
                           : 0;
                   },
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public async Task<Result<long>> GetBlockNumber(
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api?module=proxy&action=eth_blockNumber&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: (response, content) =>
                   {
                       var json = JsonConvert.DeserializeObject<JObject>(content);
                       var blockNumber = json.ContainsKey("result")
                           ? long.Parse(json["result"].ToString().Substring(prefixOffset), System.Globalization.NumberStyles.HexNumber)
                           : 0;

                       return new Result<long>(blockNumber);
                   },
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public async Task<Result<BigInteger>> GetTransactionCountAsync(
            string address,
            bool pending = true,
            CancellationToken cancellationToken = default)
        {
            var tag = pending ? "pending" : "latest";
            var requestUri = $"api?module=proxy&action=eth_getTransactionCount&address={address}&tag={tag}&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<BigInteger>(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: (response, content) =>
                   {
                       var json = JsonConvert.DeserializeObject<JObject>(content);

                       return json.ContainsKey("result")
                           ? new HexBigInteger(json["result"].ToString()).Value
                           : 0;
                   },
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public async Task<Result<BigInteger>> TryGetTransactionCountAsync(
            string address,
            bool pending = true,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionCountAsync(address, pending, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transaction count after {attempts} attempts");
        }

        public async override Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            if (txId == null)
                return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);

            var requestUri = $"api?module=proxy&action=eth_getTransactionByHash&txhash={txId}&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var tx = await HttpHelper.GetAsyncResult<IBlockchainTransaction>(
                   baseUri: BaseUrl,
                   requestUri: requestUri,
                   responseHandler: (response, content) =>
                   {
                       var tx = JsonConvert.DeserializeObject<JObject>(content)?["result"];

                       if (tx == null)
                           return null;

                       return new EthereumTransaction
                       {
                           Id            = tx["hash"].Value<string>(),
                           Currency      = Currency,
                           Type          = BlockchainTransactionType.Unknown,
                           //State = state,
                           //CreationTime = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),

                           From          = tx["from"].Value<string>().ToLowerInvariant(),
                           To            = tx["to"].Value<string>().ToLowerInvariant(),
                           Input         = tx["input"].Value<string>(),
                           Amount        = new HexBigInteger(tx["value"].Value<string>()),
                           Nonce         = tx["nonce"] != null
                                ? new HexBigInteger(tx["nonce"].Value<string>()).Value
                                : 0,
                           GasPrice      = tx["gasPrice"] != null
                                ? new HexBigInteger(tx["gasPrice"].Value<string>()).Value
                                : 0,
                           GasLimit      = new HexBigInteger(tx["gas"].Value<string>()).Value,
                           //GasUsed = 0,
                           //ReceiptStatus = state == BlockchainTransactionState.Confirmed,
                           IsInternal    = false,
                           InternalIndex = 0,

                           BlockInfo = new BlockInfo
                           {
                               //Confirmations = tx.Confirmations != null
                               //     ? int.Parse(tx.Confirmations)
                               //     : 1,
                               BlockHash   = tx["blockHash"]?.Value<string>(),
                               BlockHeight = (long)new HexBigInteger(tx["blockNumber"].Value<string>()).Value,
                               //BlockTime = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),
                               //FirstSeen = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp))
                           }
                       };
                   },
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);

            if (tx.HasError)
                return tx.Error;

            var blockNumberHex = "0x" + tx.Value.BlockInfo.BlockHeight.ToString("X");

            var blockTime = await HttpHelper.GetAsyncResult<DateTime>(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=proxy&action=eth_getBlockByNumber&tag={blockNumberHex}&boolean=true&apikey={ApiKey}",
                    responseHandler: (response, content) => {
                        var result = JsonConvert.DeserializeObject<JObject>(content);

                        var hexTimeStamp = result?["result"]?["timestamp"]?.Value<string>();

                        if (hexTimeStamp == null)
                            return DateTime.MinValue;

                        var timeStamp = (long)new HexBigInteger(hexTimeStamp).ToUlong();

                        return DateTimeExtensions.UnixStartTime.AddSeconds(timeStamp);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (blockTime.HasError)
                return blockTime.Error;

            var txReceipt = await HttpHelper.GetAsyncResult<int>(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=transaction&action=gettxreceiptstatus&txhash={txId}&apikey={ApiKey}",
                    responseHandler: (response, content) => {
                        var result = JsonConvert.DeserializeObject<JObject>(content);

                        var status = result?["result"]?["status"]?.Value<string>();

                        if (status == "")
                            return null;

                        return status != null
                            ? int.Parse(status)
                            : 0;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txReceipt == null)
                return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);

            if (txReceipt.HasError)
                return txReceipt.Error;

            tx.Value.State = txReceipt.Value == 0
                ? BlockchainTransactionState.Failed
                : BlockchainTransactionState.Confirmed;

            tx.Value.BlockInfo.Confirmations = tx.Value.State == BlockchainTransactionState.Confirmed
                ? 1
                : 0;

            tx.Value.CreationTime = blockTime.Value;
            tx.Value.BlockInfo.FirstSeen = blockTime.Value;
            tx.Value.BlockInfo.BlockTime = blockTime.Value;

            var internalTxsResult = await GetInternalTransactionsAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (internalTxsResult == null)
                return new Error(Errors.RequestError, "Connection error while getting internal transactions");

            if (internalTxsResult.HasError)
                return internalTxsResult.Error;

            if (internalTxsResult.Value.Any())
            {
                var ethTx = tx.Value as EthereumTransaction;

                ethTx.InternalTxs = internalTxsResult.Value
                    .Cast<EthereumTransaction>()
                            .ToList()
                            .ForEachDo(itx => itx.State = ethTx.State)
                            .ToList();
            }

            return tx;
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
            long fromBlock = 0,
            long toBlock = long.MaxValue,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            // todo: add pagination support
            var result = await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=account&action=txlist&address={address}&startblock={fromBlock}&endblock={toBlock}&sort=asc&apikey={ApiKey}",
                    responseHandler: (response, content) => new Result<IEnumerable<IBlockchainTransaction>>(
                        ParseTransactions(content, txId: null, isInternal: false)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result == null)
                return new Error(Errors.RequestError, "Connection error while getting transactions");

            if (result.HasError)
                return result;

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var internalResult = await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=account&action=txlistinternal&address={address}&startblock={fromBlock}&endblock={toBlock}&sort=asc&apikey={ApiKey}",
                    responseHandler: (response, content) => new Result<IEnumerable<IBlockchainTransaction>>(
                        ParseTransactions(content, txId: null, isInternal: true)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (internalResult == null)
                return new Error(Errors.RequestError, "Connection error while getting internal transactions");

            if (internalResult.HasError)
                return internalResult;

            var txs = result.Value.Concat(internalResult.Value);

            return new Result<IEnumerable<IBlockchainTransaction>>(txs);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            long fromBlock = 0,
            long toBlock = long.MaxValue,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionsAsync(address, fromBlock, toBlock, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transactions after {attempts} attempts");
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

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            string topic1,
            string topic2,
            string topic3,
            CancellationToken cancellationToken = default)
        {
            return GetContractEventsAsync(address, fromBlock, toBlock, cancellationToken, topic0, topic1, topic2, topic3);
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

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            if (!(transaction is EthereumTransaction ethTx))
                return new Error(Errors.TransactionBroadcastError, "Invalid transaction type.");

            var requestUri = $"api?module=proxy&action=eth_sendRawTransaction&hex=0x{ethTx.RlpEncodedTx}&apikey={ApiKey}";

            string txId = null;
            var attempts = 20;
            const int delayIntervalMs = 3000;

            // 11.11.2020
            // Etherscan can return OK, but the transaction is not added to the blockchain's mempool.
            // The decision to send the transaction until the response "already known" is received.
            for (var i = 0; i < attempts; ++i)
            {
                await RequestLimitControl
                    .Wait(cancellationToken)
                    .ConfigureAwait(false);

                var txIdResult = await HttpHelper.PostAsyncResult<string>(
                       baseUri: BaseUrl,
                       requestUri: requestUri,
                       content: null,
                       responseHandler: (response, content) =>
                       {
                           var json = JsonConvert.DeserializeObject<JObject>(content);

                           var error = json.SelectToken("error.message")?.Value<string>();

                           if (error != null)
                               return new Error(Errors.TransactionBroadcastError, error);

                           return json.ContainsKey("result")
                               ? json["result"].Value<string>()
                               : null;
                       },
                       cancellationToken: cancellationToken)
                   .ConfigureAwait(false);

                if (!txIdResult.HasError)
                {
                    txId = txIdResult.Value; // remember tx id

                    await Task.Delay(delayIntervalMs)
                        .ConfigureAwait(false);
                }
                else if (txIdResult.HasError && txId != null)
                {
                    // received an error, but there is already a transaction id
                    ethTx.Id = txId;
                    return txId;
                }
                else if (txIdResult.HasError)
                {
                    return txIdResult;
                }
            }

            return txId;
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
                        ? tx.IsError.Equals("0")
                            ? BlockchainTransactionState.Confirmed
                            : BlockchainTransactionState.Failed
                        : BlockchainTransactionState.Unconfirmed;

                result.Add(new EthereumTransaction
                {
                    Id            = id,
                    Currency      = Currency,
                    Type          = BlockchainTransactionType.Unknown,
                    State         = state,
                    CreationTime  = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),

                    From          = tx.From.ToLowerInvariant(),
                    To            = tx.To.ToLowerInvariant(),
                    Input         = tx.Input,
                    Amount        = BigInteger.Parse(tx.Value),
                    Nonce         = tx.Nonce != null
                        ? BigInteger.Parse(tx.Nonce)
                        : 0,
                    GasPrice      = tx.GasPrice != null
                        ? BigInteger.Parse(tx.GasPrice)
                        : 0,
                    GasLimit      = BigInteger.Parse(tx.Gas),
                    GasUsed       = BigInteger.Parse(tx.GasUsed),
                    ReceiptStatus = state == BlockchainTransactionState.Confirmed,
                    IsInternal    = isInternal,
                    InternalIndex = internalIndex,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = tx.Confirmations != null
                            ? int.Parse(tx.Confirmations)
                            : 1,
                        BlockHash   = tx.BlockHash,
                        BlockHeight = long.Parse(tx.BlockNumber),
                        BlockTime   = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp)),
                        FirstSeen   = DateTimeExtensions.UnixStartTime.AddSeconds(double.Parse(tx.TimeStamp))
                    }
                });
            }

            return result;
        }

        public async Task<Result<decimal>> GetERC20AllowanceAsync(
            EthereumTokens.ERC20 erc20,
            string tokenAddress,
            FunctionMessage allowanceMessage,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var callData = (allowanceMessage as ERC20AllowanceFunctionMessage)
                    .GetCallData()
                    .ToHex(prefix: true);

                return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: BaseUrl,
                    requestUri: $"api?module=proxy&action=eth_call&to={tokenAddress}&data={callData}&tag=latest&apikey={ApiKey}",
                    responseHandler: (response, content) =>
                    {
                        var result = JsonConvert.DeserializeObject<JObject>(content);

                        var allowanceHex = result?["result"]?.Value<string>();

                        return !string.IsNullOrEmpty(allowanceHex)
                            ? erc20.TokenDigitsToTokens(new HexBigInteger(allowanceHex).Value)
                            : 0;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        private GasPrice _gasPrice;
        private DateTime _gasPriceTimeStampUtc;

        public async Task<Result<GasPrice>> GetGasPriceAsync(
            bool useCache = true,
            CancellationToken cancellationToken = default)
        {
            if (useCache &&
                _gasPrice != null &&
                DateTime.UtcNow - _gasPriceTimeStampUtc <= TimeSpan.FromMinutes(3))
            {
                return _gasPrice;
            }

            var baseUri = "https://api.etherscan.io/";
            var requestUri = $"api?module=gastracker&action=gasoracle&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                   baseUri: baseUri,
                   requestUri: requestUri,
                   responseHandler: (response, content) =>
                   {
                       var json = JsonConvert.DeserializeObject<JObject>(content);

                       _gasPrice = new GasPrice
                       {
                           Low     = json["result"].Value<long>("SafeGasPrice"),
                           Average = json["result"].Value<long>("ProposeGasPrice"),
                           High    = json["result"].Value<long>("FastGasPrice")
                       };

                       _gasPriceTimeStampUtc = DateTime.UtcNow;

                       return json.ContainsKey("result")
                           ? new Result<GasPrice>(_gasPrice)
                           : new Result<GasPrice>(new Error(Errors.InvalidResponse, "Invalid response"));
                   },
                   cancellationToken: cancellationToken)
               .ConfigureAwait(false);
        }

        public async Task<Result<long>> GetBlockByTimeStampAsync(
            long unixTimeStamp,
            CancellationToken cancellationToken = default)
        {
            var uri = $"api?module=block&action=getblocknobytime&timestamp={unixTimeStamp}&closest=before&apikey={ApiKey}";

            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: uri,
                    responseHandler: (response, content) =>
                    {
                        var json = JsonConvert.DeserializeObject<JObject>(content);

                        return json.ContainsKey("result")
                            ? new Result<long>(json["result"].Value<long>())
                            : new Result<long>(new Error(Errors.RequestError, "Can't get blockno by timestamp"));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<long>> TryGetBlockByTimeStampAsync(
            long unixTimeStamp,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetBlockByTimeStampAsync(unixTimeStamp, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting blockno by timestamp after {attempts} attempts");
        }
    }
}