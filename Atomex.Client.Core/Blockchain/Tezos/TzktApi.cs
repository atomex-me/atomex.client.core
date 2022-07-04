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
using Atomex.Common.Memory;
using Atomex.TezosTokens;
using Atomex.Wallet.Tezos;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TzktApi : BlockchainApi, ITezosBlockchainApi, ITokenBlockchainApi
    {
        private readonly TezosConfig _currency;
        private readonly string _baseUri;
        private readonly string _rpcNodeUri;
        private readonly HttpRequestHeaders _headers;
        public const int PageSize = 10000;

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

            var requestUri = $"operations/transactions/{txId}?micheline=2";

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
            var requestUri = $"accounts/{address}/operations?type=transaction&micheline=2";

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
                    requestUri: $"operations/transactions?sender={from}&target={to}&{parameters}",
                    headers: _headers,
                    responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)),
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
                            var delegationTime = accountInfo["delegationTime"]?.ToString() ?? null;

                            return new Account
                            {
                                Address         = address,
                                DelegateAddress = accountInfo["delegate"]?["address"]?.ToString() ?? null,
                                DelegationTime  = delegationTime != null
                                    ? DateTimeOffset.Parse(delegationTime).DateTime
                                    : DateTime.MinValue,
                                DelegationLevel = accountInfo["delegationLevel"]?.Value<decimal>() ?? 0
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
            JArray data)
        {
            var result = new List<TezosTransaction>();

            foreach (var op in data)
            {
                if (op is not JObject transaction)
                    return new Error(Errors.NullOperation, "Null operation in response");

                var state = StateFromStatus(transaction["status"]?.Value<string>());

                var alias = $"{transaction["sender"]?["alias"]?.Value<string>() ?? string.Empty}/{transaction["target"]?["alias"]?.Value<string>() ?? string.Empty}";

                if (alias.Length == 1)
                    alias = string.Empty;

                var tx = new TezosTransaction()
                {
                    Id       = transaction["hash"].ToString(),
                    Currency = _currency.Name,
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
                    var txParameters = transaction.ContainsKey("parameter")
                        ? transaction["parameter"].Value<JObject>()
                        : null;

                    tx.Params       = txParameters;
                    tx.Fee          = transaction["bakerFee"].Value<decimal>();
                    tx.GasLimit     = transaction["gasLimit"].Value<decimal>();
                    tx.StorageLimit = transaction["storageLimit"].Value<decimal>();
                    tx.StorageUsed = transaction["storageUsed"].Value<decimal>();
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
            var tokenConfig = _currency as Fa12Config;

            try
            {
                var rpc = new Rpc(_rpcNodeUri);

                var tx = new TezosTransaction
                {
                    Currency          = tokenConfig.Name,
                    From              = callingAddress,
                    To                = tokenConfig.TokenContractAddress,
                    Fee               = 0, //token.GetAllowanceFee,
                    GasLimit          = tokenConfig.GetAllowanceGasLimit,
                    StorageLimit      = 0, //token.GetAllowanceStorageLimit,
                    Params            = CreateGetAllowanceParams(holderAddress, spenderAddress, tokenConfig.ViewContractAddress),

                    UseRun            = false,
                    UseOfflineCounter = false
                };

                _ = await tx
                    .FillOperationsAsync(
                        securePublicKey: securePublicKey,
                        tezosConfig: tokenConfig,
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

        public async Task<Result<bool>> IsFa2TokenOperatorActiveAsync(
            string holderAddress,
            string spenderAddress,
            string tokenContractAddress,
            int tokenId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var requestUri = $"contracts/{tokenContractAddress}/bigmaps/operators/keys/{{\"owner\":\"{holderAddress}\",\"operator\":\"{spenderAddress}\",\"token_id\":\"{tokenId}\"}}";

                var result = await HttpHelper.GetAsyncResult<JObject>(
                    baseUri: _baseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) => JsonConvert.DeserializeObject<JObject>(content),
                    cancellationToken: cancellationToken);

                if (result.HasError)
                    return false;

                return result?.Value?["active"]?.Value<bool>() ?? false;
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        #endregion

        private JObject CreateGetAllowanceParams(
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

        public async Task<Result<List<TokenBalance>>> GetTokenBalanceAsync(
            IEnumerable<string> addresses,
            IEnumerable<string> tokenContracts = null,
            IEnumerable<int> tokenIds = null,
            int offset = 0,
            int limit = PageSize,
            CancellationToken cancellationToken = default)
        {
            var hasPages = true;
            var result = new List<TokenBalance>();

            var accountsFilter = addresses.Count() == 1
                ? $"account={addresses.First()}"
                : $"account.in={string.Join(',', addresses)}";

            var tokenContractsFilter = tokenContracts != null
                ? tokenContracts.Count() == 1
                    ? $"&token.contract={tokenContracts.First()}"
                    : $"&token.contract.in={string.Join(',', tokenContracts)}"
                : "";

            var tokenIdsFilter = tokenIds != null
                ? tokenIds.Count() == 1
                    ? $"&token.tokenId={tokenIds.First()}"
                    : $"&token.tokenId.in={string.Join(',', tokenIds)}"
                : "";

            while (hasPages)
            {
                var requestUri = $"tokens/balances?" +
                    accountsFilter +
                    tokenContractsFilter +
                    tokenIdsFilter +
                    $"&offset={offset}" +
                    $"&limit={limit}";

                var res = await HttpHelper
                    .GetAsyncResult<List<TokenBalanceResponse>>(
                        baseUri: _baseUri,
                        requestUri: requestUri,
                        responseHandler: (response, content) => JsonConvert.DeserializeObject<List<TokenBalanceResponse>>(content),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (res.HasError)
                    return res.Error;

                if (res.Value.Any())
                {
                    result.AddRange(res.Value.Select(x => x.ToTokenBalance()));
                    offset += res.Value.Count;

                    if (res.Value.Count < PageSize)
                        hasPages = false;
                }
                else
                {
                    hasPages = false;
                }
            }

            return result;
        }

        public async Task<Result<List<TokenTransfer>>> GetTokenTransfersAsync(
            IEnumerable<string> addresses,
            IEnumerable<string> tokenContracts = null,
            IEnumerable<int> tokenIds = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int offset = 0,
            int limit = PageSize,
            CancellationToken cancellationToken = default)
        {
            var tokenContractsFilter = tokenContracts != null
                ? tokenContracts.Count() == 1
                    ? $"&token.contract={tokenContracts.First()}"
                    : $"&token.contract.in={string.Join(',', tokenContracts)}"
                : "";

            var tokenIdsFilter = tokenIds != null
                ? tokenIds.Count() == 1
                    ? $"&token.tokenId={tokenIds.First()}"
                    : $"&token.tokenId.in={string.Join(',', tokenIds)}"
                : "";

            var fromTimeStampFilter = from != null
                ? $"&timestamp.gt={from.Value.ToIso8601()}"
                : "";

            var toTimeStampFilter = to != null
                ? $"&timestamp.le={to.Value.ToIso8601()}"
                : "";

            // todo: use `anyof.from.to.in` after release in TzKT
            var accountsFilters = addresses.Count() == 1
                ? new string[] {
                    $"anyof.from.to={addresses.First()}"
                }
                : new string[] {
                    $"from.in={string.Join(',', addresses)}",
                    $"to.in={string.Join(',', addresses)}"
                };

            var transfers = new List<TokenTransfer>();

            // todo: use `anyof.from.to.in` after release in TzKT
            foreach (var accountFilter in accountsFilters)
            {
                var hasPages = true;
                var transfersCount = 0;

                while (hasPages && transfersCount < limit)
                {
                    var requestLimit = Math.Min(limit - transfersCount, PageSize);

                    var requestUri = $"tokens/transfers?" +
                        accountFilter +
                        tokenContractsFilter +
                        tokenIdsFilter +
                        fromTimeStampFilter +
                        toTimeStampFilter +
                        $"&offset={offset}" +
                        $"&limit={requestLimit}";

                    var tokenTransfersRes = await HttpHelper
                        .GetAsyncResult<List<TokenTransferResponse>>(
                            baseUri: _baseUri,
                            requestUri: requestUri,
                            responseHandler: (_, content) => JsonConvert.DeserializeObject<List<TokenTransferResponse>>(content),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (tokenTransfersRes.HasError)
                        return tokenTransfersRes.Error;

                    if (tokenTransfersRes.Value.Any())
                    {
                        var operationTxIdsString = string.Join(',', tokenTransfersRes.Value
                            .Select(tokenTransfer => tokenTransfer.TransactionId)
                            .Distinct()
                            .ToList());

                        var tokenOperationsRes = await HttpHelper
                            .GetAsyncResult<List<TokenOperation>>(
                                baseUri: _baseUri,
                                requestUri: $"operations/transactions?id.in={operationTxIdsString}&select=hash,counter,nonce,id",
                                responseHandler: (_, content) => JsonConvert.DeserializeObject<List<TokenOperation>>(content),
                                cancellationToken: cancellationToken)
                            .ConfigureAwait(false);

                        if (tokenOperationsRes.HasError)
                            return tokenOperationsRes.Error;

                        transfers.AddRange(tokenTransfersRes.Value.Select(tokenTransfer =>
                            {
                                var tokenOperation = tokenOperationsRes.Value
                                    .Find(to => to.Id == tokenTransfer.TransactionId);

                                return tokenTransfer.ToTokenTransfer(
                                    tokenOperation?.Hash ?? string.Empty,
                                    tokenOperation?.Counter ?? 0,
                                    tokenOperation?.Nonce
                                );
                            })
                        );

                        transfersCount += tokenTransfersRes.Value.Count;
                        offset += tokenTransfersRes.Value.Count;

                        if (tokenTransfersRes.Value.Count < limit)
                            hasPages = false;
                    }
                    else
                    {
                        hasPages = false;
                    }
                }
            }

            return transfers
                .Distinct(new Common.EqualityComparer<TokenTransfer>(
                    (t1,t2) => t1.Id.Equals(t2.Id),
                    t => t.Id.GetHashCode()))
                .ToList();
        }
    }
}