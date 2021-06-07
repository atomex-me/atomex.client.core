using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.TezosTokens;
using Atomex.Wallet.Tezos;

namespace Atomex.Blockchain.Tezos
{
    public class TzktApi : BlockchainApi, ITezosBlockchainApi, ITokenBlockchainApi
    {
        private readonly TezosConfig _currency;
        private readonly string _baseUri;
        private readonly string _rpcNodeUri;
        private readonly HttpRequestHeaders _headers;

        private class TxsSource
        {
            public string BaseUri { get; set; }
            public string RequestUri { get; set; }
            public Func<string, Result<IEnumerable<TezosTransaction>>> Parser { get; set; }
        }

        public TzktApi(TezosConfig currency)
        {
            _currency = currency;
            _baseUri = currency.BaseUri;
            _rpcNodeUri = currency.RpcNodeUri;

            _headers = new HttpRequestHeaders
            {
                new KeyValuePair<string, IEnumerable<string>>("User-Agent", new string[] {"Atomex"})
            };
        }

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            var tx = (TezosTransaction)transaction;

            try
            {
                tx.State = BlockchainTransactionState.Pending;

                var rpc = new Rpc(_rpcNodeUri);
                var readyForInject = true;

                if (tx.UsePreApply)
                {
                    var opResults = await rpc
                        .PreApplyOperations(tx.Head, tx.Operations, tx.SignedMessage.EncodedSignature)
                        .ConfigureAwait(false);

                    if (!opResults.Any())
                        return new Error(Errors.EmptyPreApplyOperations, "Empty pre apply operations");

                    foreach (var opResult in opResults)
                        Log.Debug("OperationResult {@result}: {@opResult}", opResult.Succeeded, opResult.Data.ToString());

                    readyForInject = opResults.Any() && opResults.All(op => op.Succeeded);
                }

                string txId = null;

                if (readyForInject)
                {
                    var injectedOperation = await rpc
                        .InjectOperations(tx.SignedMessage.SignedBytes)
                        .ConfigureAwait(false);

                    if (injectedOperation != null)
                        Log.Debug($"Injection result: {injectedOperation}");

                    txId = injectedOperation.ToString();
                }

                if (txId == null)
                {
                    tx.RollbackOfflineCounterIfNeed();

                    return new Error(Errors.NullTxId, "Null tx id");
                }

                tx.Id = txId;

                return tx.Id;
            }
            catch (Exception e)
            {
                Log.Error(e, $"Broadcast error: {e.Message}");

                tx.RollbackOfflineCounterIfNeed();

                return new Error(Errors.RequestError, e.Message);
            }
        }

        public override async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"accounts/{address}";

            return await HttpHelper.GetAsyncResult<decimal>(
                baseUri: _baseUri,
                requestUri: requestUri,
                headers: _headers,
                responseHandler: (response, content) =>
                {
                    var addressInfo = JsonConvert.DeserializeObject<JObject>(content);

                    var type = addressInfo["type"].Value<string>();

                    if (type == "empty")
                        return 0;

                    return addressInfo["balance"].Value<decimal>().ToTez();
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var transaction = new TezosTransaction();
            var isParentTxValid = false;

            var requestUri = $"operations/transactions/{txId}";

            return await HttpHelper
                .GetAsyncResult<IBlockchainTransaction>(
                    baseUri: _baseUri,
                    requestUri: requestUri,
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var txResult = ParseTxs(JsonConvert.DeserializeObject<JArray>(content));

                        if (txResult.HasError)
                            return txResult.Error;

                        if (!txResult.Value.Any())
                            return null;

                        var internalTxs = new List<TezosTransaction>();

                        foreach (var tx in txResult.Value)
                        {
                            if (!tx.IsInternal)
                            {
                                transaction = tx;
                                isParentTxValid = true;
                            }
                            else
                                internalTxs.Add(tx);
                        }

                        transaction.InternalTxs = internalTxs;

                        // replace non valid parent tx by internal tx
                        if (isParentTxValid == false && internalTxs.Count == 1)
                            transaction = internalTxs.First();

                        return transaction;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"accounts/{address}/operations?type=transaction";

            var txsResult = await HttpHelper
                .GetAsyncResult(
                    baseUri: _baseUri,
                    requestUri: requestUri,
                    headers: _headers,
                    responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
                return new Error(Errors.RequestError, $"Connection error while getting input transactions for address {address}");

            if (txsResult.HasError)
                return txsResult.Error;

            return new Result<IEnumerable<IBlockchainTransaction>>(txsResult.Value);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionsAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transactions after {attempts} attempts");
        }

        public async Task<Result<IEnumerable<TezosTransaction>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper
                .GetAsyncResult(
                    baseUri: _baseUri,
                    requestUri: $"operations/transactions?sender={from}&target={to}&parameters.eq={parameters}",
                    headers: _headers,
                    responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content), parseTokenParams: false),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<TezosTransaction>>> TryGetTransactionsAsync(
            string from,
            string to,
            string parameters,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionsAsync(from, to, parameters, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transactions after {attempts} attempts");
        }

        public async Task<Result<TezosAddressInfo>> GetAddressInfoAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<TezosAddressInfo>(
                    baseUri: _baseUri,
                    requestUri: $"accounts/{address}",
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var addressInfo = JsonConvert.DeserializeObject<JObject>(content);

                        var type = addressInfo["type"].Value<string>();

                        if (type == "empty")
                        {
                            return new TezosAddressInfo
                            {
                                Address          = address,
                                IsAllocated      = false,
                                IsRevealed       = false,
                                LastCheckTimeUtc = DateTime.UtcNow
                            };
                        }

                        if (type == "user")
                        {
                            return new TezosAddressInfo
                            {
                                Address          = address,
                                IsAllocated      = decimal.Parse(addressInfo["balance"].Value<string>()) > 0,
                                IsRevealed       = addressInfo["revealed"].Value<bool>(),
                                LastCheckTimeUtc = DateTime.UtcNow
                            };
                        }

                        return new TezosAddressInfo
                        {
                            Address          = address,
                            IsAllocated      = true,
                            IsRevealed       = true,
                            LastCheckTimeUtc = DateTime.UtcNow
                        };
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<Account>> GetAccountByAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<Account>(
                    baseUri: _baseUri,
                    requestUri: $"accounts/{address}",
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var accountInfo = JsonConvert.DeserializeObject<JObject>(content);

                        var type = accountInfo["type"].Value<string>();

                        if (type == "user")
                        {
                            return new Account
                            {
                                Address         = address,
                                DelegateAddress = accountInfo["delegate"]["address"].ToString(),
                                DelegationTime  = DateTimeOffset.Parse(accountInfo["delegationTime"].ToString()).DateTime,
                                DelegationLevel = accountInfo["delegationLevel"].Value<decimal>()
                            };
                        }

                        return null;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<decimal>> GetHeadLevelAsync(
             CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: _baseUri,
                    requestUri: $"head",
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var head = JsonConvert.DeserializeObject<JObject>(content);

                        return head["level"].Value<decimal>();
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<bool>> IsAllocatedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var addressInfo = await GetAddressInfoAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (addressInfo == null)
                return null;

            if (addressInfo.HasError)
                return addressInfo.Error;

            return addressInfo.Value.IsAllocated;
        }

        public async Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var addressInfo = await GetAddressInfoAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (addressInfo == null)
                return null;

            if (addressInfo.HasError)
                return addressInfo.Error;

            return addressInfo.Value.IsRevealed;
        }

        private Result<IEnumerable<TezosTransaction>> ParseTxs(
            JArray data,
            bool parseTokenParams = true)
        {
            var result = new List<TezosTransaction>();

            foreach (var op in data)
            {
                if (!(op is JObject transaction))
                    return new Error(Errors.NullOperation, "Null operation in response");

                var state = StateFromStatus(transaction["status"].Value<string>());

                var alias = $"{transaction["sender"]["alias"]?.Value<string>()}/{transaction["target"]["alias"]?.Value<string>()}";

                if (alias.Length == 1)
                    alias = string.Empty;

                var tx = new TezosTransaction()
                {
                    Id       = transaction["hash"].ToString(),
                    Currency = _currency,
                    State    = state,
                    Type     = BlockchainTransactionType.Unknown,
                    CreationTime = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc),

                    GasUsed = transaction["gasUsed"].Value<decimal>(),
                    Burn = transaction["storageFee"].Value<decimal>() +
                           transaction["allocationFee"].Value<decimal>(),

                    Alias = alias,

                    IsInternal = transaction.ContainsKey("nonce"),
                    //tx.IsInternal = tx.From == ((TezosTokens.FA12) _currency).SwapContractAddress;
                    InternalIndex = transaction["nonce"]?.Value<int>() ?? 0,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = state == BlockchainTransactionState.Failed ? 0 : 1,
                        BlockHash     = null,
                        BlockHeight   = transaction["level"].Value<long>(),
                        BlockTime     = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc),
                        FirstSeen     = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc)
                    }
                };


                tx.From   = transaction["sender"]?["address"]?.ToString();
                tx.To     = transaction["target"]?["address"]?.ToString();
                tx.Amount = transaction["amount"].Value<decimal>();
                tx.Alias  = alias;

                if (tx.IsInternal)
                {
                    tx.InternalIndex = transaction["nonce"]?.Value<int>() ?? 0;
                }
                else
                {
                    var txParameters = transaction.ContainsKey("parameters")
                        ? JObject.Parse(transaction["parameters"].Value<string>())
                        : null;

                    tx.Params       = txParameters;
                    tx.Fee          = transaction["bakerFee"].Value<decimal>();
                    tx.GasLimit     = transaction["gasLimit"].Value<decimal>();
                    tx.StorageLimit = transaction["storageLimit"].Value<decimal>();
                }

                if (tx != null)
                    result.Add(tx);
            }

            return result;
        }

        public static BlockchainTransactionState StateFromStatus(string status) =>
            status switch
            {
                "applied"     => BlockchainTransactionState.Confirmed,
                "backtracked" => BlockchainTransactionState.Failed,
                "skipped"     => BlockchainTransactionState.Failed,
                "failed"      => BlockchainTransactionState.Failed,
                _             => BlockchainTransactionState.Unknown
            };

        #region ITokenBlockchainApi

        public async Task<Result<decimal>> GetFa12AllowanceAsync(
            string holderAddress,
            string spenderAddress,
            string callingAddress,
            SecureBytes securePublicKey,
            CancellationToken cancellationToken = default)
        {
            var token = _currency as Fa12Config;

            try
            {
                var rpc = new Rpc(_rpcNodeUri);

                var tx = new TezosTransaction
                {
                    Currency          = token,
                    From              = callingAddress,
                    To                = token.TokenContractAddress,
                    Fee               = 0, //token.GetAllowanceFee,
                    GasLimit          = token.GetAllowanceGasLimit,
                    StorageLimit      = 0, //token.GetAllowanceStorageLimit,
                    Params            = GetAllowanceParams(holderAddress, spenderAddress, token.ViewContractAddress),

                    UseRun            = false,
                    UseOfflineCounter = false
                };

                await tx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var runResults = await rpc
                    .RunOperations(tx.Head, tx.Operations)
                    .ConfigureAwait(false);

                return runResults
                    ?["contents"]
                    ?.LastOrDefault()
                    ?["metadata"]
                    ?["internal_operation_results"]
                    ?[0]
                    ?["result"]
                    ?["errors"]
                    ?[1]
                    ?["with"]
                    ?["args"]
                    ?[0]
                    ?["args"]
                    ?[0]
                    ?["int"]
                    ?.Value<decimal>() ?? 0;
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public async Task<Result<decimal>> TryGetFa12AllowanceAsync(
            string holderAddress,
            string spenderAddress,
            string callingAddress,
            SecureBytes securePublicKey,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetFa12AllowanceAsync(holderAddress, spenderAddress, callingAddress, securePublicKey, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting balance after {attempts} attempts");
        }

        #endregion

        private JObject GetAllowanceParams(
            string holderAddress,
            string spenderAddress,
            string viewContractAddress)
        {
            return JObject.FromObject(new
            {
                entrypoint = "getAllowance",
                value = new
                {
                    args = new object[]
                    {
                        new
                        {
                            args = new object[]
                            {
                                new
                                {
                                    @string = holderAddress
                                },
                                new
                                {
                                    @string = spenderAddress
                                }
                            },
                            prim = "Pair"
                        },
                        new
                        {
                            @string = viewContractAddress + "%viewNat"
                        }
                    },
                    prim = "Pair"
                }
            });
        }
    }
}