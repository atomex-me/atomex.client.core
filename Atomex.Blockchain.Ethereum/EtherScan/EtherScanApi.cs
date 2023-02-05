﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;

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
            var requestUri = "api?" +
                "module=account" +
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

            var responseObj = JsonSerializer.Deserialize<Response<string>>(content);

            if (responseObj == null || responseObj.Message != "OK" || responseObj.Result == null)
                return new Error(Errors.GetBalanceError, "Invalid response");

            return BigInteger.Parse(responseObj.Result);
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
            var requestUri = "api?" +
                "module=proxy" +
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

                var rpcResponse = JsonSerializer.Deserialize<RpcResponse<string>>(content);

                if (rpcResponse == null)
                    return new Error(Errors.BroadcastError, "Rpc response is null");

                if (rpcResponse.Result != null)
                {
                    transactionId = rpcResponse.Result; // save tx id

                    await Task.Delay(broadcastDelayIntervalMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (rpcResponse.Result == null && transactionId != null)
                {
                    return transactionId; // received an error, but there is already a transaction id
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
            var requestUri = "api?" +
                "module=account" +
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

            var responseObj = JsonSerializer.Deserialize<Response<string>>(content);

            if (responseObj == null || responseObj.Message != "OK")
                return new Error((int)response.StatusCode, "Error response");

            if (responseObj.Result == null)
                return new Error(Errors.GetErc20BalanceError, "Invalid response");

            return BigInteger.Parse(responseObj.Result);
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
                var requestUri = "api?" +
                    "module=account" +
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

                var transfersResponse = JsonSerializer.Deserialize<Response<List<Erc20TransferDto>>>(content);

                if (transfersResponse == null || transfersResponse.Message != "OK" || transfersResponse.Result == null)
                    return new Error(Errors.GetErc20TransactionsError, "Invalid response");

                var transfers = transfersResponse.Result;

                var received = transfers.Count();

                foreach (var transfer in transfers)
                {
                    var erc20transfer = new Erc20Transfer
                    {
                        From = transfer.From,
                        To = transfer.To,
                        Value = BigInteger.Parse(transfer.Value),
                    };

                    var hash = transfer.Hash;

                    if (!txsIndex.TryGetValue(hash, out var erc20tx))
                    {
                        var timeStamp = DateTimeOffset
                            .FromUnixTimeSeconds(long.Parse(transfer.TimeStamp));

                        var confirmations = long.Parse(transfer.Confirmations);

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
                            BlockHeight   = long.Parse(transfer.BlockNumber),
                            Nonce         = long.Parse(transfer.Nonce),
                            GasPrice      = long.Parse(transfer.GasPrice),
                            GasLimit      = long.Parse(transfer.Gas),
                            GasUsed       = long.Parse(transfer.GasUsed),
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

                var requestUri = $"api?" +
                    $"module=proxy" +
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

                var allowanceResponse = JsonSerializer.Deserialize<RpcResponse<string>>(content);

                if (allowanceResponse == null)
                    return new Error(Errors.GetFa12AllowanceError, "Invalid response");

                return !string.IsNullOrEmpty(allowanceResponse.Result)
                    ? new HexBigInteger(allowanceResponse.Result).Value
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

            var requestUri = "api?" +
                "module=proxy" +
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

            var transactionCountResponse = JsonSerializer.Deserialize<RpcResponse<string>>(content);

            if (transactionCountResponse == null)
                return new Error(Errors.GetTransactionsCountError, "Invalid response");

            return transactionCountResponse.Result != null
                ? new HexBigInteger(transactionCountResponse.Result).Value
                : 0;
        }

        #endregion

        public async Task<Result<RpcTransactionReceipt>> GetTransactionReceiptAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?" +
                "module=proxy" +
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

            var txReceiptResponse = JsonSerializer.Deserialize<RpcResponse<RpcTransactionReceipt>>(content);

            if (txReceiptResponse == null)
                return new Error(Errors.GetTransactionReceiptError, "Invalid response");

            return txReceiptResponse.Result;
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

            var (txReceipt, txReceiptError) = await GetTransactionReceiptAsync(
                    txId: txId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txReceiptError != null)
                return txReceiptError;

            var status = txReceipt?.Status != null
                ? (int?)int.Parse(txReceipt.Status)
                : null;

            tx.Status = status == null
                ? TransactionStatus.Pending
                : status == 0
                    ? TransactionStatus.Failed
                    : TransactionStatus.Confirmed;

            tx.GasUsed = txReceipt?.GasUsed != null
                ? new HexBigInteger(txReceipt.Status).Value
                : 0;

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
            var requestUri = "api?" +
                "module=proxy" +
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

            var txResponse = JsonSerializer.Deserialize<RpcResponse<RpcTransaction>>(content);

            if (txResponse == null || txResponse.Result == null)
                return new Error(Errors.GetTransactionError, "Invalid response");

            var tx = new EthereumTransaction
            {
                Id = txResponse.Result.Hash,
                Currency = EthereumHelper.Eth,
                CreationTime = DateTimeOffset.UtcNow, // todo: fix to received time
                BlockHeight = txResponse.Result.BlockNumber != null
                    ? (long)new HexBigInteger(txResponse.Result.BlockNumber).Value
                    : 0,
                From = txResponse.Result.From,
                To = txResponse.Result.To,
                // ChainId
                Amount = new HexBigInteger(txResponse.Result.Value),
                Nonce = txResponse.Result.Nonce != null
                    ? new HexBigInteger(txResponse.Result.Nonce).Value
                    : 0,
                GasPrice = txResponse.Result.GasPrice != null
                    ? new HexBigInteger(txResponse.Result.GasPrice).Value
                    : 0,
                GasLimit = new HexBigInteger(txResponse.Result.Gas).Value,
                Data = txResponse.Result.Input
                // IsError
                // ErrorDescription
                // InternalTransactions
            };

            return tx;
        }

        public async Task<Result<long>> GetRecentBlockHeightAsync(
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api?" +
                $"module=proxy" +
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

            var blockNumberResponse = JsonSerializer.Deserialize<Response<string>>(content);

            if (blockNumberResponse == null || blockNumberResponse.Message != "OK" || blockNumberResponse.Result == null)
                return new Error(Errors.GetRecentBlockHeightError, "Invalid response");

            return (long)new HexBigInteger(blockNumberResponse.Result).Value;
        }

        public async Task<Result<RpcBlock>> GetBlockByNumberAsync(
            long blockHeight,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?" +
                "module=proxy" +
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

            var blockResponse = JsonSerializer.Deserialize<RpcResponse<RpcBlock>>(content);

            if (blockResponse == null || blockResponse.Result == null)
                return new Error(Errors.GetBlockError, "Block is null");

            return blockResponse.Result;
        }

        public async Task<Result<long>> GetBlockTimeAsync(
            long blockHeight,
            CancellationToken cancellationToken = default)
        {
            var (block, error) = await GetBlockByNumberAsync(
                    blockHeight,
                    cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (block == null)
                return new Error(Errors.GetBlockError, "Block is null");

            return (long)new HexBigInteger(block.TimeStamp).Value;
        }

        public async Task<Result<ulong>> GetBlockNumberAsync(
            DateTimeOffset timeStamp,
            ClosestBlock blockClosest = ClosestBlock.After,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?" +
                "module=block" +
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

            var blockNumberResponse = JsonSerializer.Deserialize<Response<string>>(content);

            if (blockNumberResponse == null || blockNumberResponse.Message != "OK" || blockNumberResponse.Result == null)
                return new Error(Errors.GetBlockNumberError, "Invalid response");

            return ulong.Parse(blockNumberResponse.Result);
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
                var requestUri = "api?" +
                    "module=account" +
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

                var txsResponse = JsonSerializer.Deserialize<Response<List<TransactionDto>>>(content);

                if (txsResponse == null || txsResponse.Message != "OK" || txsResponse.Result == null)
                    return new Error(Errors.GetTransactionsError, "Invalid response");

                var received = txsResponse.Result.Count();

                foreach (var t in txsResponse.Result)
                {
                    var timeStamp = DateTimeOffset
                        .FromUnixTimeSeconds(long.Parse(t.TimeStamp));

                    var confirmations = long.Parse(t.Confirmations);

                    var isError = bool.Parse(t.IsError);

                    var txReceiptStatus = t.TxReceiptStatus != ""
                        ? bool.Parse(t.TxReceiptStatus)
                        : !isError;

                    var status = isError
                        ? TransactionStatus.Failed
                        : confirmations > 0
                            ? TransactionStatus.Confirmed
                            : TransactionStatus.Pending;

                    var tx = new EthereumTransaction
                    {
                        Id = t.Hash,
                        Currency = EthereumHelper.Eth,
                        CreationTime = timeStamp,
                        BlockTime = timeStamp,
                        BlockHeight = t.BlockNumber != null
                            ? long.Parse(t.BlockNumber)
                            : 0,
                        Confirmations = confirmations,
                        Status = status,

                        // ChainId
                        From = t.From,
                        To = t.To,
                        Amount = BigInteger.Parse(t.Value),
                        Nonce = t.Nonce != null
                            ? BigInteger.Parse(t.Nonce)
                            : 0,
                        GasPrice = t.GasPrice != null
                            ? BigInteger.Parse(t.GasPrice)
                            : 0,
                        GasLimit = BigInteger.Parse(t.Gas),
                        GasUsed = BigInteger.Parse(t.GasUsed),
                        Data = t.Input,
                        IsError = isError,

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
                var requestUri = "api?" +
                    "module=account" +
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

                var txsResponse = JsonSerializer.Deserialize<Response<List<InternalTransactionDto>>>(content);

                if (txsResponse == null || txsResponse.Message != "OK" || txsResponse.Result == null)
                    return new Error(Errors.GetTransactionsError, "Invalid response");

                var received = txsResponse.Result.Count();

                txs.AddRange(ParseInternalTransactions(txsResponse.Result));

                if (received < offset)
                    break;
            }

            return txs;
        }

        public async Task<Result<IEnumerable<EthereumInternalTransaction>>> GetInternalTransactionsByHashAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = "api?" +
                "module=account" +
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

            var txsResponse = JsonSerializer.Deserialize<Response<List<InternalTransactionDto>>>(content);

            if (txsResponse == null || txsResponse.Message != "OK" || txsResponse.Result == null)
                return new Error(Errors.GetInternalTransactionsError, "Invalid response");

            return ParseInternalTransactions(txsResponse.Result);
        }

        public static List<EthereumInternalTransaction> ParseInternalTransactions(
            List<InternalTransactionDto> txsArray,
            string? txId = null)
        {
            var internalTxs = new List<EthereumInternalTransaction>();

            foreach (var internalTx in txsArray)
            {
                var timeStamp = DateTimeOffset
                    .FromUnixTimeSeconds(long.Parse(internalTx.TimeStamp))
                    .UtcDateTime;

                var tx = new EthereumInternalTransaction
                {
                    BlockHeight      = long.Parse(internalTx.BlockNumber),
                    BlockTime        = timeStamp,
                    Hash             = txId ?? internalTx.Hash,
                    From             = internalTx.From,
                    To               = internalTx.To,
                    Value            = BigInteger.Parse(internalTx.Value),
                    GasLimit         = BigInteger.Parse(internalTx.Gas),
                    Data             = internalTx.Input,
                    Type             = internalTx.Type,
                    IsError          = bool.Parse(internalTx.IsError),
                    ErrorDescription = internalTx.ErrCode
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
            var requestUri = "api?" +
                "module=gastracker" +
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

            var gasPriceResponse = JsonSerializer.Deserialize<Response<GasPrice>>(content);

            if (gasPriceResponse == null || gasPriceResponse.Message != "OK" || gasPriceResponse.Result == null)
                return new Error(Errors.GetGasPriceError, "Invalid response");

            return gasPriceResponse.Result;
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
            var requestUri = "api?" +
                "module=proxy" +
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

            var estimateGasResponse = JsonSerializer.Deserialize<RpcResponse<string>>(content);

            if (estimateGasResponse == null)
                return new Error(Errors.EstimateGasError, "Invalid response");

            return estimateGasResponse.Result != null
                ? (long)new HexBigInteger(estimateGasResponse.Result).Value
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

            var contractEventsResponse = JsonSerializer
                .Deserialize<Response<List<ContractEvent>>>(content);

            if (contractEventsResponse == null)
                return new Error(Errors.GetContractEventsError, "Error while get contracts");

            return new Result<IEnumerable<ContractEvent>>
            {
                Value = contractEventsResponse.Result ?? Enumerable.Empty<ContractEvent>()
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