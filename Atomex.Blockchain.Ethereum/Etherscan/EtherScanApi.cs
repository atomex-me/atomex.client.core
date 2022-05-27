using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Hex.HexTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Ethereum.Abstract;
using Atomex.Blockchain.Ethereum.Etherscan.Swaps.V1;
using Atomex.Blockchain.Ethereum.Erc20;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum.Etherscan
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
        public string ApiToken { get; set; }
        public int RequestLimitDelayMs { get; set; } = 500;
        public List<EtherScanContractSettings> Contracts { get; set; }

        public ulong GetBlock(string contractAddress) =>
            Contracts?.FirstOrDefault(s => s.Address == contractAddress)?.Block ?? 0;

        public string GetTokenContract(string token) =>
            Contracts?.FirstOrDefault(s => s.Token == token)?.Address;
    }

    public class EtherScanApi : IEthereumApi, IErc20Api, IBlockchainSwapApi
    {
        public const string Uri = "https://api.etherscan.io/";

        private static RequestLimitControl _rlcInstance;
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

        public async Task<(BigInteger balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=account" +
                "&action=balance" +
                $"&address={address}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (
                    balance: 0,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            var balance = result != null
                ? BigInteger.Parse(result.Value<string>())
                : 0;

            var error = result == null
                ? new Error(Errors.GetBalanceError, "Invalid response")
                : null;

            return (balance, error);
        }

        public async Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var (tx, error) = await GetTransactionAsync(
                    txId: txId,
                    includeInternals: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return (tx, error);
        }

        public async Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            if (transaction is not EthereumTransaction ethereumTransaction)
                return (txId: null, error: new Error(Errors.BroadcastError, "Invalid transaction type"));

            var requestUri = "api?module=proxy" +
                "&action=eth_sendRawTransaction" +
                $"&hex=0x{ethereumTransaction.GetRlpEncoded()}" +
                $"&apikey={Settings.ApiToken}";

            var broadcastAttempts = 20;
            var broadcastDelayIntervalMs = 3000;
            string transactionId = null;

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
                    return (
                        txId: null,
                        error: new Error((int)response.StatusCode, "Error status code received"));

                var json = JsonConvert.DeserializeObject<JObject>(content);

                var errorMessage = json
                    .SelectToken("error.message")
                    ?.Value<string>();

                if (errorMessage != null)
                    return (txId: null, new Error(Errors.BroadcastError, errorMessage));

                var result = json["result"];

                var txId = result?.Value<string>();

                var error = result == null
                    ? new Error(Errors.BroadcastError, "TxId is null")
                    : null;

                if (error == null)
                {
                    transactionId = txId; // save tx id

                    await Task.Delay(broadcastDelayIntervalMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (error != null && transactionId != null)
                {
                    return (txId: transactionId, error: null); // received an error, but there is already a transaction id
                }
                else // error != null
                {
                    return (txId: null, error);
                }
            }

            return (
                txId: transactionId,
                error: transactionId == null
                    ? new Error(Errors.BroadcastError, "TxId is null")
                    : null
            );
        }

        #endregion IBlockchainApi

        #region IEthereumApi

        public async Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
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
                return (txs: null, error);

            return await GetTransactionsAsync(
                    address: address,
                    fromBlock: fromBlock.Value,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<(IEnumerable<EthereumTransaction> txs, Error error)> GetTransactionsAsync(
            string address,
            ulong fromBlock = 0,
            ulong toBlock = ulong.MaxValue,
            CancellationToken cancellationToken = default)
        {
            var (txs, txsError) = await GetNormalTransactionsAsync(
                    address: address,
                    fromBlock: fromBlock,
                    toBlock: toBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsError != null)
                return (txs: null, error: txsError);

            var (internalTxs, internalTxsError) = await GetInternalTransactionsAsync(
                    address: address,
                    fromBlock: fromBlock,
                    toBlock: toBlock,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (internalTxsError != null)
                return (txs: null, error: internalTxsError);

            foreach (var internalTx in internalTxs)
            {
                if (txs.Index.TryGetValue(internalTx.Hash, out var existsTx))
                {
                    if (existsTx.InternalTransactions == null)
                        existsTx.InternalTransactions = new List<EthereumInternalTransaction>();

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
                        return (txs: null, error: txError);

                    // update txs list and txs index
                    txs.Transactions.Add(tx);
                    txs.Index.Add(tx.TxId, tx);
                }
            }

            return (txs: txs.Transactions, error: null);
        }

        public async Task<(long? count, Error error)> GetTransactionsCountAsync(
            string address,
            bool pending = false,
            CancellationToken cancellationToken = default)
        {
            var tag = pending ? "pending" : "latest";

            var requestUri = "api?module=proxy" +
                "&action=eth_getTransactionCount" +
                $"&address={address}" +
                $"&tag={tag}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (
                    count: null,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            var count = result != null
                ? (long)new HexBigInteger(result.ToString()).Value
                : 0;

            return (count, error: null);
        }

        public async Task<(decimal? gasPrice, Error error)> GetFastGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            var (gasPrice, error) = await GetOracleGasPriceAsync(cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (gasPrice: null, error);

            return (gasPrice.Fast, error: null);
        }

        public async Task<(int? estimatedGas, Error error)> EstimateGasAsync(
            string to,
            string from = null,
            BigInteger? value = null,
            BigInteger? gasPrice = null,
            BigInteger? gasLimit = null,
            string data = null,
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

            var response = await HttpHelper
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
                return (
                    estimatedGas: null,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            var estimatedGas = result != null
                ? (int)new HexBigInteger(result.ToString()).Value
                : 0;

            return (estimatedGas, error: null);
        }

        #endregion IEthereumApi

        #region IErc20Api

        public async Task<(BigInteger balance, Error error)> GetErc20BalanceAsync(
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

            var response = await HttpHelper
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
                return (
                    balance: BigInteger.Zero,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];

            var balance = result != null
                ? BigInteger.Parse(result.Value<string>())
                : 0;

            var error = result == null
                ? new Error(Errors.GetErc20BalanceError, "Invalid response")
                : null;

            return (balance, error);
        }

        public async Task<(IEnumerable<Erc20Transaction> txs, Error error)> GetErc20TransactionsAsync(
            string address,
            string token,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            var (fromBlock, fromBlockError) = await GetBlockNumberAsync(
                    timeStamp: fromTimeStamp,
                    blockClosest: ClosestBlock.After,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (fromBlockError != null)
                return (txs: null, error: fromBlockError);

            var (txs, error) = await GetErc20TransactionsAsync(
                    address: address,
                    token: token,
                    fromBlock: fromBlock.Value,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs: txs.Transactions, error: null);
        }

        public async Task<(TransactionsResult<Erc20Transaction> txs, Error error)> GetErc20TransactionsAsync(
            string address,
            string token,
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
                    $"&contractaddress={Settings.GetTokenContract(token)}" +
                    $"&address={address}" +
                    $"&startblock={fromBlock}" +
                    $"&endblock={toBlockStr}" +
                    $"&page={page}" +
                    $"&offset={offset}" +
                    $"&sort=asc" +
                    $"&apikey={Settings.ApiToken}";

                var response = await HttpHelper
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
                    return (
                        txs: null,
                        error: new Error((int)response.StatusCode, "Error status code received"));

                var result = JsonConvert.DeserializeObject<JObject>(content)
                    ?["result"];

                if (result == null)
                    return (
                        txs: null,
                        error: new Error(Errors.GetErc20TransactionsError, "Null data received"));

                var received = result.Count();

                foreach (var transfer in result)
                {
                    var erc20transfer = new Erc20Transfer
                    {
                        From  = transfer["from"].Value<string>(),
                        To    = transfer["to"].Value<string>(),
                        Value = BigInteger.Parse(transfer["value"].Value<string>())
                    };

                    var hash = transfer["hash"].Value<string>();

                    if (!txsIndex.TryGetValue(hash, out var erc20tx))
                    {
                        var timeStamp = DateTimeOffset
                            .FromUnixTimeSeconds(result["timeStamp"].Value<long>());

                        var confirmations = result["confirmations"].Value<long>();

                        erc20tx = new Erc20Transaction
                        {
                            TxId         = hash,
                            Currency     = token,
                            Status       = confirmations > 0
                                ? TransactionStatus.Confirmed
                                : TransactionStatus.Pending,
                            CreationTime = timeStamp,
                            BlockTime    = timeStamp,
                            BlockHeight  = result["blockNumber"].Value<long>(),

                            Nonce        = result["nonce"].Value<long>(),
                            GasPrice     = result["gasPrice"].Value<long>(),
                            GasLimit     = result["gas"].Value<long>(),
                            GasUsed      = result["gasUsed"].Value<long>(),
                            Transfers    = new List<Erc20Transfer>()
                        };

                        txsIndex.Add(hash, erc20tx);
                        txs.Add(erc20tx);
                    }

                    erc20tx.Transfers.Add(erc20transfer);
                }

                if (received < offset)
                    break;
            }

            return (
                txs: new TransactionsResult<Erc20Transaction> {
                    Transactions = txs,
                    Index = txsIndex
                },
                error: null);
        }

        #endregion IErc20Api

        #region IBlockchainSwapApi

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindLocksAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await EtherScanSwapHelper
                .FindLocksAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: Settings.GetBlock(contractAddress),
                    address: address,
                    timeStamp: timeStamp,
                    lockTime: lockTime,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindAdditionalLocksAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await EtherScanSwapHelper
                .FindAdditionalLocksAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: Settings.GetBlock(contractAddress),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindRedeemsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await EtherScanSwapHelper
                .FindRedeemsAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: Settings.GetBlock(contractAddress),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindRefundsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            var (txs, error) = await EtherScanSwapHelper
                .FindRefundsAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    contractBlock: Settings.GetBlock(contractAddress),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        #endregion IBlockchainSwapApi

        #region Common

        public async Task<(GasPrice gasPrice, Error error)> GetOracleGasPriceAsync(
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=gastracker" +
                "&action=gasoracle" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (gasPrice: null, error: new Error((int)response.StatusCode, "Error status code received"));

            var json = JsonConvert.DeserializeObject<JObject>(content);

            var result = json["result"];
            var hasResult = result != null;

            var gasPrice = hasResult
                ? result.ToObject<GasPrice>()
                : null;

            var error = !hasResult
                ? new Error(Errors.GetGasPriceError, "Invalid response")
                : null;

            return (gasPrice, error);
        }

        public async Task<(int? status, Error error)> GetReceiptStatusAsync(
            string txId,
            Action<JToken> handler = null,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_getTransactionReceipt" +
                $"&txhash={txId}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (status: null, error: new Error((int)response.StatusCode, "Error status code received"));

            var txReceipt = JsonConvert.DeserializeObject<JObject>(content);

            if (txReceipt?["status"]?.Value<string>() == "0")
                return (
                    status: null,
                    error: new Error(
                        Errors.GetReceiptStatusError,
                        $"Request error code: {txReceipt?["message"]?.Value<string>()}. " +
                        $"Description: {txReceipt?["result"]?.Value<string>()}")
                );

            var result = txReceipt?["result"];

            if (result == null)
                return (status: null, error: new Error(Errors.GetReceiptStatusError, "Null data received"));

            handler?.Invoke(result);

            var status = result?["status"] != null
                ? (int)new HexBigInteger(result?["status"]?.Value<string>()).Value
                : 0;

            return (status, error: null);
        }

        public async Task<(long? blockHeight, Error error)> GetRecentBlockHeightAsync(
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api?module=proxy" +
                $"&action=eth_blockNumber" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (blockHeight: null, error: new Error((int)response.StatusCode, "Error status code received"));

            var blockNumber = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"];

            if (blockNumber == null)
                return (blockHeight: null, error: new Error(Errors.GetRecentBlockHeightError, "Block number is null"));

            var blockHeight = (long)new HexBigInteger(blockNumber.Value<string>()).Value;

            return (blockHeight, error: null);
        }

        public async Task<(long? timeStamp, Error error)> GetBlockTimeAsync(
            long blockHeight,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_getBlockByNumber" +
                "&boolean=false" +
                $"&tag=0x{blockHeight:X}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (
                    timeStamp: null,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var timeStampJson = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"]
                ?["timestamp"];

            if (timeStampJson == null)
                return (
                    timeStamp: null,
                    error: new Error(Errors.GetRecentBlockHeightError, "Block timestamp is null"));

            var timeStamp = (long)new HexBigInteger(timeStampJson.Value<string>()).Value;

            return (timeStamp, error: null);
        }

        public async Task<(ulong? blockNumber, Error error)> GetBlockNumberAsync(
            DateTimeOffset timeStamp,
            ClosestBlock blockClosest = ClosestBlock.After,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=block" +
                "&action=getblocknobytime" +
                $"&timestamp={timeStamp.ToUnixTimeSeconds()}" +
                $"&closest={blockClosest.ToString().ToLower()}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (
                    blockNumber: null,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var blockNumberJson = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"];

            if (blockNumberJson == null)
                return (
                    blockNumber: null,
                    error: new Error(Errors.GetBlockNumberError, "Block number is null"));

            return (blockNumberJson.Value<ulong>(), error: null);
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
                    result += $"&topic{i-1}_{i}_opr={operation.ToString().ToLowerInvariant()}";

                result += $"&topic{i}={topics[i]}";
            }

            return result;
        }

        public static string BlockNumberToTag(
            ulong blockNumber) =>
            blockNumber switch
            {
                ulong.MaxValue => "latest",
                _ => blockNumber.ToString()
            };

        public async Task<(IEnumerable<ContractEvent> events, Error error)> GetContractEventsAsync(
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

            var response = await HttpHelper
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
                return (
                    events: null,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var events = JsonConvert
                .DeserializeObject<Response<List<ContractEvent>>>(content)
                .Result;

            return (events, error: null);
        }

        public Task<(IEnumerable<ContractEvent> events, Error error)> GetContractEventsAsync(
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

        public Task<(IEnumerable<ContractEvent> events, Error error)> GetContractEventsAsync(
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

        public Task<(IEnumerable<ContractEvent> events, Error error)> GetContractEventsAsync(
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

        public Task<(IEnumerable<ContractEvent> events, Error error)> GetContractEventsAsync(
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

        public async Task<(EthereumTransaction tx, Error error)> GetTransactionAsync(
            string txId,
            bool includeInternals = true,
            CancellationToken cancellationToken = default)
        {
            var (tx, txError) = await GetTransactionByHashAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (txError != null)
                return (tx: null, error: txError);

            var (status, statusError) = await GetReceiptStatusAsync(
                    txId: txId,
                    handler: result =>
                    {
                        tx.GasUsed = new HexBigInteger(result?["status"]?.Value<string>()).Value;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (statusError != null)
                return (tx: null, error: txError);

            tx.Status = status == null
                ? TransactionStatus.Pending
                : status == 0
                    ? TransactionStatus.Canceled
                    : TransactionStatus.Confirmed;

            if (tx.BlockHeight != 0)
            {
                var (blockTime, blockTimeError) = await GetBlockTimeAsync(tx.BlockHeight, cancellationToken)
                    .ConfigureAwait(false);

                if (blockTimeError != null)
                    return (tx: null, error: blockTimeError);

                tx.BlockTime = DateTimeOffset
                    .FromUnixTimeSeconds(blockTime.Value);

                var (recentBlockHeight, recentBlockHeightError) = await GetRecentBlockHeightAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (recentBlockHeightError != null)
                    return (tx: null, error: recentBlockHeightError);

                tx.Confirmations = recentBlockHeight.Value - tx.BlockHeight;
            }

            if (includeInternals)
            {
                var (internalTxs, internalTxsError) = await GetInternalTransactionsByHashAsync(
                        txId: txId,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (internalTxsError != null)
                    return (tx: null, error: internalTxsError);

                tx.InternalTransactions = internalTxs?.ToList();
            }

            return (tx, error: null);
        }

        public async Task<(EthereumTransaction tx, Error error)> GetTransactionByHashAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=proxy" +
                "&action=eth_getTransactionByHash" +
                $"&txhash={txId}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (
                    tx: null,
                    error: new Error((int)response.StatusCode, "Error status code received."));

            var result = JsonConvert.DeserializeObject<JObject>(content)
                ?["result"];

            if (result == null)
                return (
                    tx: null,
                    error: new Error(Errors.GetTransactionError, "Null data received."));

            var tx = new EthereumTransaction
            {
                TxId         = result["hash"].Value<string>(),
                Currency     = EthereumHelper.Eth,
                CreationTime = DateTimeOffset.UtcNow,
                BlockHeight  = result["blockNumber"] != null
                    ? (long)new HexBigInteger(result["blockNumber"].Value<string>()).Value
                    : 0,

                From         = result["from"].Value<string>(),
                To           = result["to"].Value<string>(),
                // ChainId
                Amount       = new HexBigInteger(result["value"].Value<string>()),
                Nonce        = result["nonce"] != null
                    ? new HexBigInteger(result["nonce"].Value<string>()).Value
                    : 0,
                GasPrice     = result["gasPrice"] != null
                    ? new HexBigInteger(result["gasPrice"].Value<string>()).Value
                    : 0,
                GasLimit     = new HexBigInteger(result["gas"].Value<string>()).Value,
                Data         = result["input"]?.Value<string>()
                // IsError
                // ErrorDescription
                // InternalTransactions
            };

            return (tx, error: null);
        }

        public async Task<(TransactionsResult<EthereumTransaction> txs, Error error)> GetNormalTransactionsAsync(
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

                var response = await HttpHelper
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
                    return (
                        txs: null,
                        error: new Error((int)response.StatusCode, "Error status code received"));

                var result = JsonConvert.DeserializeObject<JObject>(content)
                    ?["result"];

                if (result == null)
                    return (
                        txs: null,
                        error: new Error(Errors.GetTransactionsError, "Null data received"));

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
                        ? TransactionStatus.Canceled
                        : confirmations > 0
                            ? TransactionStatus.Confirmed
                            : TransactionStatus.Pending;

                    var tx = new EthereumTransaction
                    {
                        TxId         = t["hash"].Value<string>(),
                        Currency     = EthereumHelper.Eth,
                        CreationTime = timeStamp,
                        BlockTime    = timeStamp,
                        BlockHeight  = t["blockNumber"] != null
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
                    txsIndex.Add(tx.TxId, tx);
                }

                if (received < offset)
                    break;
            }

            return (
                txs: new TransactionsResult<EthereumTransaction>
                {
                    Transactions = txs,
                    Index        = txsIndex
                },
                error: null);
        }

        public async Task<(IEnumerable<EthereumInternalTransaction> txs, Error error)> GetInternalTransactionsAsync(
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

                var response = await HttpHelper
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
                    return (
                        txs: null,
                        error: new Error((int)response.StatusCode, "Error status code received"));

                var result = JsonConvert.DeserializeObject<JObject>(content)
                    ?["result"];

                if (result == null)
                    return (
                        txs: null,
                        error: new Error(Errors.GetTransactionsError, "Null data received"));

                var received = result.Count();

                txs.AddRange(ParseInternalTransactions(result));

                if (received < offset)
                    break;
            }

            return (txs, error: null);
        }

        public async Task<(IEnumerable<EthereumInternalTransaction> txs, Error error)> GetInternalTransactionsByHashAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?module=account" +
                "&action=txlistinternal" +
                $"&txhash={txId}" +
                $"&apikey={Settings.ApiToken}";

            var response = await HttpHelper
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
                return (
                    txs: null,
                    error: new Error((int)response.StatusCode, "Error status code received"));

            var result = JsonConvert.DeserializeObject<JObject>(content)?["result"];

            if (result == null)
                return (
                    txs: null,
                    error: new Error(Errors.GetInternalTransactionsError, "Null data received"));

            return (ParseInternalTransactions(result), error: null);
        }

        public static List<EthereumInternalTransaction> ParseInternalTransactions(
            JToken txsArray,
            string txId = null)
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

        #endregion Common
    }
}