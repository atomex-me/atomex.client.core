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

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Blockchain.Ethereum.Erc20.Abstract;
using Atomex.Blockchain.Ethereum.Erc20.Messages;
using Atomex.Common;
using Error = Atomex.Common.Error;

namespace Atomex.Blockchain.Ethereum.EtherScan
{
    public class EtherScanContractSettings
    {
        public string Address { get; set; }
        public ulong Block { get; set; }
        public string Token { get; set; }
        public bool IsToken => Token != null;
    }

    public class EtherScanSettings
    {
        public string BaseUri { get; set; } = EtherScanApi.Uri;
        public string? ApiToken { get; set; }
        public int RequestLimitDelayMs { get; set; } = 500;
        public List<EtherScanContractSettings>? Contracts { get; set; }

        public ulong GetBlock(string contractAddress) =>
            Contracts?.FirstOrDefault(s => s.Address == contractAddress)?.Block ?? 0;

        public string? GetTokenContract(string token) =>
            Contracts?.FirstOrDefault(s => s.Token == token)?.Address;
    }

    public class EtherScanApi : IBlockchainApi, IErc20Api, IEthereumApi, IGasPriceProvider
    {
        public const string Uri = "https://api.etherscan.io/";

        private static RequestLimitControl? _rlcInstance;
        private static RequestLimitControl GetRequestLimitControl(int delayMs)
        {
            var instance = _rlcInstance;

            if (instance == null)
            {
                Interlocked.CompareExchange(ref _rlcInstance, new RequestLimitControl(delayMs), null);
                instance = _rlcInstance;
            }

            return instance;
        }

        public EtherScanSettings Settings { get; set; }

        public EtherScanApi(EtherScanSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #region IBlockchainApi

        public async Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=account" +
                "&action=balance" +
                $"&address={address}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            if (result == null)
                return new Error(Errors.GetBalanceError, "Invalid response");

            return BigInteger.Parse(result.Value<string>());
        }

        public async Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var (tx, error) = await GetTransactionAsync(
                    txId: txId,
                    includeInternals: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (tx == null)
                return new Error(Errors.GetTransactionError, "Tx is null");

            return tx;
        }

        public async Task<Result<string>> BroadcastAsync(
            EthereumTransactionRequest txRequest,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_sendRawTransaction" +
                $"&hex=0x{txRequest.GetRlpEncoded()}" +
                $"&apikey={Settings.ApiToken}";

            var broadcastAttempts = 20;
            var broadcastDelayIntervalMs = 3000;
            string? transactionId = null;

            // EtherScan may return OK, but the transaction is not added to the mempool.
            // The solution is to send the transaction until a "already known" response is received.
            for (var i = 0; i < broadcastAttempts; ++i)
            {
                var response = await HttpHelper
                    .PostAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        content: null,
                        requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var json = JsonConvert.DeserializeObject<JObject>(content);

                var errorMessage = json
                    .SelectToken("error.message")
                    ?.Value<string>();

                if (errorMessage != null)
                    return new Error(Errors.BroadcastError, errorMessage);

                var result = json["result"];

                if (result != null)
                {
                    transactionId = result?.Value<string>(); // save tx id

                    await Task.Delay(broadcastDelayIntervalMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (result == null && transactionId != null)
                {
                    return transactionId!; // received an error, but there is already a transaction id
                }
                else // result == null, error
                {
                    return new Error(Errors.BroadcastError, "TxId is null");
                }
            }

            if (transactionId == null)
                return new Error(Errors.BroadcastError, "TxId is null");

            return transactionId;
        }

        #endregion

        #region IErc20Api

        public async Task<Result<BigInteger>> GetErc20BalanceAsync(
            string address,
            string token,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=account" +
                "&action=tokenbalance" +
                $"&contractaddress={Settings.GetTokenContract(token)}" +
                $"&address={address}" +
                $"&tag=latest" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            if (result == null)
                return new Error(Errors.GetErc20BalanceError, "Invalid response");

            return BigInteger.Parse(result.Value<string>());
        }

        public async Task<Result<IEnumerable<Erc20Transaction>>> GetErc20TransactionsAsync(
            string address,
            string tokenContractAddress,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            var (fromBlock, fromBlockError) = await GetBlockNumberAsync(
                    timeStamp: fromTimeStamp,
                    blockClosest: ClosestBlock.After,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (fromBlockError != null)
                return fromBlockError;

            var (txs, error) = await GetErc20TransactionsAsync(
                    address: address,
                    tokenContractAddress: tokenContractAddress,
                    fromBlock: fromBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (txs == null)
                return new Error(Errors.GetErc20TransactionsError, "Txs is null");

            return txs.Transactions;
        }

        public async Task<Result<TransactionsResult<Erc20Transaction>>> GetErc20TransactionsAsync(
            string address,
            string tokenContractAddress,
            ulong fromBlock = ulong.MinValue,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default)
        {
            var toBlockStr = BlockNumberToTag(toBlock);
            var offset = 10000;

            var txs = new List<Erc20Transaction>();
            var txsIndex = new Dictionary<string, Erc20Transaction>();

            for (var page = 1; ; page++)
            {
                var requestUri = "api?module=account" +
                    "&action=tokentx" +
                    $"&contractaddress={Settings.GetTokenContract(tokenContractAddress)}" +
                    $"&address={address}" +
                    $"&startblock={fromBlock}" +
                    $"&endblock={toBlockStr}" +
                    $"&page={page}" +
                    $"&offset={offset}" +
                    $"&sort=asc" +
                    $"&apikey={Settings.ApiToken}";

                using var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var result = JsonConvert.DeserializeObject<JObject>(content)
                    ?["result"];

                if (result == null)
                    return new Error(Errors.GetErc20TransactionsError, "Null data received");

                var received = result.Count();

                foreach (var transfer in result)
                {
                    var erc20transfer = new Erc20Transfer
                    {
                        From  = transfer["from"].Value<string>(),
                        To    = transfer["to"].Value<string>(),
                        Value = BigInteger.Parse(transfer["value"].Value<string>()),
                        Type  = TransactionType.Unknown
                    };

                    var hash = transfer["hash"].Value<string>();

                    if (!txsIndex.TryGetValue(hash, out var erc20tx))
                    {
                        var timeStamp = DateTimeOffset
                            .FromUnixTimeSeconds(result["timeStamp"].Value<long>());

                        var confirmations = result["confirmations"].Value<long>();

                        erc20tx = new Erc20Transaction
                        {
                            Id            = hash,
                            Currency      = tokenContractAddress,
                            Status        = confirmations > 0
                                ? TransactionStatus.Confirmed
                                : TransactionStatus.Pending,
                            Confirmations = confirmations,
                            CreationTime  = timeStamp,
                            BlockTime     = timeStamp,
                            BlockHeight   = result["blockNumber"].Value<long>(),
                            Nonce         = result["nonce"].Value<long>(),
                            GasPrice      = result["gasPrice"].Value<long>(),
                            GasLimit      = result["gas"].Value<long>(),
                            GasUsed       = result["gasUsed"].Value<long>(),
                            Transfers     = new List<Erc20Transfer>()
                        };

                        txsIndex.Add(hash, erc20tx);
                        txs.Add(erc20tx);
                    }

                    erc20tx.Transfers.Add(erc20transfer);
                }

                if (received < offset)
                    break;
            }

            return new TransactionsResult<Erc20Transaction>
            {
                Transactions = txs,
                Index = txsIndex
            };
        }

        public async Task<Result<BigInteger>> GetErc20AllowanceAsync(
            string tokenAddress,
            Erc20AllowanceMessage allowanceMessage,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var callData = allowanceMessage
                    .GetCallData()
                    .ToHex(prefix: true);

                var requestUri = $"api?module=proxy" +
                    $"&action=eth_call" +
                    $"&to={tokenAddress}" +
                    $"&data={callData}" +
                    $"&tag=latest" +
                    $"&apikey={Settings.ApiToken}";

                using var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var result = JsonConvert.DeserializeObject<JObject>(content);

                var allowanceHex = result?["result"]?.Value<string>();

                return !string.IsNullOrEmpty(allowanceHex)
                    ? new HexBigInteger(allowanceHex).Value
                    : 0;
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        #endregion

        #region IEthereumApi

        public async Task<Result<IEnumerable<EthereumTransaction>>> GetTransactionsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            var (fromBlock, error) = await GetBlockNumberAsync(
                    timeStamp: fromTimeStamp,
                    blockClosest: ClosestBlock.After,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return await GetTransactionsAsync(
                    address: address,
                    fromBlock: fromBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<EthereumTransaction>>> GetTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = long.MaxValue,
            CancellationToken cancellationToken = default)
        {
            var (txs, txsError) = await GetNormalTransactionsAsync(
                    address: address,
                    fromBlock: fromBlock,
                    toBlock: toBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsError != null)
                return txsError;

            if (txs == null)
                return new Error(Errors.GetTransactionsError, "Txs is null");

            var (internalTxs, internalTxsError) = await GetInternalTransactionsAsync(
                    address: address,
                    fromBlock: fromBlock,
                    toBlock: toBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (internalTxsError != null)
                return internalTxsError;

            if (internalTxs == null)
                return new Error(Errors.GetTransactionsError, "Internal txs is null");

            foreach (var internalTx in internalTxs)
            {
                if (txs!.Index.TryGetValue(internalTx.Hash, out var existsTx))
                {
                    existsTx.InternalTransactions ??= new List<EthereumInternalTransaction>();

                    existsTx.InternalTransactions.Add(internalTx);
                }
                else
                {
                    // get 'parent' transaction
                    var (tx, txError) = await GetTransactionAsync(
                            txId: internalTx.Hash,
                            includeInternals: true,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (txError != null)
                        return txError;

                    if (tx == null)
                        return new Error(Errors.GetTransactionsError, "Parent tx is null");

                    // update txs list and txs index
                    txs.Transactions.Add(tx);
                    txs.Index.Add(tx.Id, tx);
                }
            }

            return txs.Transactions;
        }

        public async Task<Result<BigInteger>> GetTransactionsCountAsync(
            string address,
            bool pending = true,
            CancellationToken cancellationToken = default)
        {
            var tag = pending ? "pending" : "latest";

            var requestUri = "api?module=proxy" +
                "&action=eth_getTransactionCount" +
                $"&address={address}" +
                $"&tag={tag}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            var count = result != null
                ? new HexBigInteger(result.ToString()).Value
                : 0;

            return count;
        }

        #endregion

        public async Task<Result<int?>> GetReceiptStatusAsync(
            string txId,
            Action<JToken>? handler = null,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_getTransactionReceipt" +
                $"&txhash={txId}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var txReceipt = JsonConvert.DeserializeObject<JObject>(content);

            if (txReceipt?["status"]?.Value<string>() == "0")
            {
                return new Error(
                    Errors.GetReceiptStatusError,
                    $"Request error code: {txReceipt?["message"]?.Value<string>()}. " +
                    $"Message: {txReceipt?["result"]?.Value<string>()}");
            }

            var result = txReceipt?["result"];

            if (result == null)
                return new Error(Errors.GetReceiptStatusError, "Null data received");

            handler?.Invoke(result);

            return result?["status"] != null
                ? (int?)new HexBigInteger(result?["status"]?.Value<string>()).Value
                : null;
        }

        public async Task<Result<EthereumTransaction>> GetTransactionAsync(
            string txId,
            bool includeInternals = true,
            CancellationToken cancellationToken = default)
        {
            var (tx, txError) = await GetTransactionByHashAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (txError != null)
                return txError;

            if (tx == null)
                return new Error(Errors.GetTransactionError, "Tx is null");

            var (status, statusError) = await GetReceiptStatusAsync(
                    txId: txId,
                    handler: result =>
                    {
                        tx.GasUsed = new HexBigInteger(result?["status"]?.Value<string>()).Value;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (statusError != null)
                return statusError;

            tx.Status = status == null
                ? TransactionStatus.Pending
                : status == 0
                    ? TransactionStatus.Failed
                    : TransactionStatus.Confirmed;

            if (tx.BlockHeight != 0)
            {
                var (blockTime, blockTimeError) = await GetBlockTimeAsync(tx.BlockHeight, cancellationToken)
                    .ConfigureAwait(false);

                if (blockTimeError != null)
                    return blockTimeError;

                tx.BlockTime = DateTimeOffset
                    .FromUnixTimeSeconds(blockTime);

                var (recentBlockHeight, recentBlockHeightError) = await GetRecentBlockHeightAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (recentBlockHeightError != null)
                    return recentBlockHeightError;

                tx.Confirmations = recentBlockHeight - tx.BlockHeight;
            }

            if (includeInternals)
            {
                var (internalTxs, internalTxsError) = await GetInternalTransactionsByHashAsync(
                        txId: txId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (internalTxsError != null)
                    return internalTxsError;

                tx.InternalTransactions = internalTxs?.ToList();
            }

            return tx;
        }

        private async Task<Result<EthereumTransaction>> GetTransactionByHashAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_getTransactionByHash" +
                $"&txhash={txId}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var result = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"];

            if (result == null)
                return new Error(Errors.GetTransactionError, "Null data received");

            var tx = new EthereumTransaction
            {
                Id           = result["hash"].Value<string>(),
                Currency     = EthereumHelper.Eth,
                CreationTime = DateTimeOffset.UtcNow, // todo: fix to received time
                BlockHeight  = result["blockNumber"] != null
                    ? (long)new HexBigInteger(result["blockNumber"].Value<string>()).Value
                    : 0,

                From   = result["from"].Value<string>(),
                To     = result["to"].Value<string>(),
                // ChainId
                Amount   = new HexBigInteger(result["value"].Value<string>()),
                Nonce    = result["nonce"] != null
                    ? new HexBigInteger(result["nonce"].Value<string>()).Value
                    : 0,
                GasPrice = result["gasPrice"] != null
                    ? new HexBigInteger(result["gasPrice"].Value<string>()).Value
                    : 0,
                GasLimit = new HexBigInteger(result["gas"].Value<string>()).Value,
                Data     = result["input"]?.Value<string>()
                // IsError
                // ErrorDescription
                // InternalTransactions
            };

            return tx;
        }

        public async Task<Result<long>> GetRecentBlockHeightAsync(
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api?module=proxy" +
                $"&action=eth_blockNumber" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var blockNumber = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"];

            if (blockNumber == null)
                return new Error(Errors.GetRecentBlockHeightError, "Block number is null");

            var blockHeight = (long)new HexBigInteger(blockNumber.Value<string>()).Value;

            return blockHeight;
        }

        public async Task<Result<long>> GetBlockTimeAsync(
            long blockHeight,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_getBlockByNumber" +
                "&boolean=false" +
                $"&tag=0x{blockHeight:X}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var timeStampJson = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"]
                ?["timestamp"];

            if (timeStampJson == null)
                return new Error(Errors.GetRecentBlockHeightError, "Block timestamp is null");

            var timeStamp = (long)new HexBigInteger(timeStampJson.Value<string>()).Value;

            return timeStamp;
        }

        public async Task<Result<ulong>> GetBlockNumberAsync(
            DateTimeOffset timeStamp,
            ClosestBlock blockClosest = ClosestBlock.After,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=block" +
                "&action=getblocknobytime" +
                $"&timestamp={timeStamp.ToUnixTimeSeconds()}" +
                $"&closest={blockClosest.ToString().ToLower()}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var blockNumberJson = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"];

            if (blockNumberJson == null)
                return new Error(Errors.GetBlockNumberError, "Block number is null");

            return blockNumberJson.Value<ulong>();
        }

        public async Task<Result<TransactionsResult<EthereumTransaction>>> GetNormalTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default)
        {
            var toBlockStr = BlockNumberToTag(toBlock);
            var offset = 10000;

            var txs = new List<EthereumTransaction>();
            var txsIndex = new Dictionary<string, EthereumTransaction>();

            for (var page = 1; ; page++)
            {
                var requestUri = "api?module=account" +
                    "&action=txlist" +
                    $"&address={address}" +
                    $"&startblock={fromBlock}" +
                    $"&endblock={toBlockStr}" +
                    $"&page={page}" +
                    $"&offset={offset}" +
                    $"&sort=asc" +
                    $"&apikey={Settings.ApiToken}";

                using var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var result = JsonConvert.DeserializeObject<JObject>(content)
                    ?["result"];

                if (result == null)
                    return new Error(Errors.GetTransactionsError, "Null data received");

                var received = result.Count();

                foreach (var t in result)
                {
                    var timeStamp = DateTimeOffset
                        .FromUnixTimeSeconds(t["timeStamp"].Value<long>());

                    var confirmations = t["confirmations"].Value<long>();

                    var isError = bool.Parse(t["isError"].Value<string>());

                    var txReceiptStatusValue = t["txreceipt_status"].Value<string>();

                    var txReceiptStatus = txReceiptStatusValue != ""
                        ? bool.Parse(txReceiptStatusValue)
                        : !isError;

                    var status = isError
                        ? TransactionStatus.Failed
                        : confirmations > 0
                            ? TransactionStatus.Confirmed
                            : TransactionStatus.Pending;

                    var tx = new EthereumTransaction
                    {
                        Id            = t["hash"].Value<string>(),
                        Currency      = EthereumHelper.Eth,
                        CreationTime  = timeStamp,
                        BlockTime     = timeStamp,
                        BlockHeight   = t["blockNumber"] != null
                            ? t["blockNumber"].Value<long>()
                            : 0,
                        Confirmations = confirmations,
                        Status        = status,

                        // ChainId
                        From     = t["from"].Value<string>(),
                        To       = t["to"].Value<string>(),
                        Amount   = BigInteger.Parse(t["value"].Value<string>()),
                        Nonce    = t["nonce"] != null
                            ? BigInteger.Parse(t["nonce"].Value<string>())
                            : 0,
                        GasPrice = t["gasPrice"] != null
                            ? BigInteger.Parse(t["gasPrice"].Value<string>())
                            : 0,
                        GasLimit = BigInteger.Parse(t["gas"].Value<string>()),
                        GasUsed  = BigInteger.Parse(t["gasUsed"].Value<string>()),
                        Data     = t["input"]?.Value<string>(),
                        IsError  = isError,

                        // ErrorDescription,
                        // InternalTransactions
                    };

                    txs.Add(tx);
                    txsIndex.Add(tx.Id, tx);
                }

                if (received < offset)
                    break;
            }

            return new TransactionsResult<EthereumTransaction>
            {
                Transactions = txs,
                Index = txsIndex
            };
        }

        public async Task<Result<IEnumerable<EthereumInternalTransaction>>> GetInternalTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default)
        {
            var toBlockStr = BlockNumberToTag(toBlock);
            var offset = 10000;

            var txs = new List<EthereumInternalTransaction>();

            for (var page = 1; ; page++)
            {
                var requestUri = "api?module=account" +
                    "&action=txlistinternal" +
                    $"&address={address}" +
                    $"&startblock={fromBlock}" +
                    $"&endblock={toBlockStr}" +
                    $"&page={page}" +
                    $"&offset={offset}" +
                    $"&sort=asc" +
                    $"&apikey={Settings.ApiToken}";

                using var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var result = JsonConvert.DeserializeObject<JObject>(content)
                    ?["result"];

                if (result == null)
                    return new Error(Errors.GetTransactionsError, "Null data received");

                var received = result.Count();

                txs.AddRange(ParseInternalTransactions(result));

                if (received < offset)
                    break;
            }

            return txs;
        }

        public async Task<Result<IEnumerable<EthereumInternalTransaction>>> GetInternalTransactionsByHashAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=account" +
                "&action=txlistinternal" +
                $"&txhash={txId}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var result = JsonConvert.DeserializeObject<JObject>(content)?["result"];

            if (result == null)
                return new Error(Errors.GetInternalTransactionsError, "Null data received");

            return ParseInternalTransactions(result);
        }

        public static List<EthereumInternalTransaction> ParseInternalTransactions(
            JToken txsArray,
            string? txId = null)
        {
            var internalTxs = new List<EthereumInternalTransaction>();

            foreach (var internalTx in txsArray)
            {
                var timeStamp = DateTimeOffset
                    .FromUnixTimeSeconds(internalTx["timeStamp"].Value<long>())
                    .UtcDateTime;

                var tx = new EthereumInternalTransaction
                {
                    BlockHeight      = internalTx["blockNumber"].Value<long>(),
                    BlockTime        = timeStamp,
                    Hash             = txId ?? internalTx["hash"].Value<string>(),
                    From             = internalTx["from"].Value<string>(),
                    To               = internalTx["to"].Value<string>(),
                    Value            = BigInteger.Parse(internalTx["value"].Value<string>()),
                    GasLimit         = BigInteger.Parse(internalTx["gas"].Value<string>()),
                    Data             = internalTx["input"].Value<string>(),
                    Type             = internalTx["type"].Value<string>(),
                    IsError          = bool.Parse(internalTx["isError"].Value<string>()),
                    ErrorDescription = internalTx["errCode"].Value<string>()
                };

                internalTxs.Add(tx);
            }

            return internalTxs;
        }

        public static string CombineTopics(
            TopicOperation operation,
            params string[] topics)
        {
            if (topics == null)
                return string.Empty;

            var result = string.Empty;

            for (var i = 0; i < topics.Length; ++i)
            {
                if (topics[i] == null)
                    continue;

                if (i != 0)
                    result += $"&topic{i - 1}_{i}_opr={operation.ToString().ToLowerInvariant()}";

                result += $"&topic{i}={topics[i]}";
            }

            return result;
        }

        public static string BlockNumberToTag(ulong blockNumber) =>
            blockNumber switch
            {
                ulong.MaxValue => "latest",
                _ => blockNumber.ToString()
            };

        public async Task<Result<long>> GetFastGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            var (gasPrice, error) = await GetOracleGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (gasPrice == null)
                return new Error(Errors.GetGasPriceError, "Oracle gas price is null");

            return gasPrice.Fast;
        }

        public async Task<Result<GasPrice>> GetOracleGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=gastracker" +
                "&action=gasoracle" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];
            var hasResult = result != null;

            if (!hasResult)
                return new Error(Errors.GetGasPriceError, "Invalid response");

            return result!.ToObject<GasPrice>();
        }

        public async Task<Result<long>> EstimateGasAsync(
            string to,
            string? from = null,
            BigInteger? value = null,
            BigInteger? gasPrice = null,
            BigInteger? gasLimit = null,
            string? data = null,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_estimateGas" +
                $"&to={to}" +
                (from != null ? $"&from={from}" : "") +
                (gasLimit != null ? $"&gas={new HexBigInteger(gasLimit.Value).HexValue}" : "") +
                (gasPrice != null ? $"&gasPrice={new HexBigInteger(gasPrice.Value).HexValue}" : "") +
                (value != null ? $"&value={new HexBigInteger(value.Value).HexValue}" : "") +
                (data != null ? $"&data={data}" : "") +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            return result != null
                ? (long)new HexBigInteger(result.ToString()).Value
                : 0;
        }

        #region ContractEvents

        public async Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock = ulong.MinValue,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default,
            params string[] topics)
        {
            var fromBlockStr = BlockNumberToTag(fromBlock);
            var toBlockStr = BlockNumberToTag(toBlock);
            var topicsStr = CombineTopics(TopicOperation.And, topics);

            var uri = "api?module=logs" +
                "&action=getLogs" +
                $"&address={address}" +
                $"&fromBlock={fromBlockStr}" +
                $"&toBlock={toBlockStr}{topicsStr}" +
                $"&apikey={Settings.ApiToken}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: uri,
                    requestLimitControl: GetRequestLimitControl(Settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var events = JsonConvert
                .DeserializeObject<Response<List<ContractEvent>>>(content)
                .Result;

            return new Result<IEnumerable<ContractEvent>>
            {
                Value = events ?? Enumerable.Empty<ContractEvent>()
            };
        }

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            CancellationToken cancellationToken = default) =>
            GetContractEventsAsync(
                address,
                fromBlock,
                toBlock,
                cancellationToken,
                topic0);

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            string topic1,
            CancellationToken cancellationToken = default) =>
            GetContractEventsAsync(
                address,
                fromBlock,
                toBlock,
                cancellationToken,
                topic0,
                topic1);

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            string topic1,
            string topic2,
            CancellationToken cancellationToken = default) =>
            GetContractEventsAsync(
                address,
                fromBlock,
                toBlock,
                cancellationToken,
                topic0,
                topic1,
                topic2);

        public Task<Result<IEnumerable<ContractEvent>>> GetContractEventsAsync(
            string address,
            ulong fromBlock,
            ulong toBlock,
            string topic0,
            string topic1,
            string topic2,
            string topic3,
            CancellationToken cancellationToken = default) =>
            GetContractEventsAsync(
                address,
                fromBlock,
                toBlock,
                cancellationToken,
                topic0,
                topic1,
                topic2,
                topic3);

        #endregion
    }
}