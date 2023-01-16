using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Operations;

namespace Atomex.Blockchain.Tezos.Tzkt
{
    public class TzktTokenContractSettings
    {
        public string Address { get; set; }
        public string Token { get; set; }
    }

    public class TzktSettings
    {
        public string BaseUri { get; set; } = TzktApi.Uri;
        public Dictionary<string, string> Headers { get; set; }
        public List<TzktTokenContractSettings> TokenContracts { get; set; }

        public string? GetTokenContract(string token) =>
            TokenContracts?.FirstOrDefault(s => s.Token == token)?.Address;
    }

    public class TzktApi : IBlockchainApi, ITezosApi
    {
        public const string Uri = "https://api.tzkt.io/v1/";
        public const int PageSize = 10000;

        public TzktSettings Settings { get; set; }

        public TzktApi(TzktSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (account == null)
                return new Error(Errors.InvalidResponse, $"[TzktApi] Account for address {address} is null");

            return (BigInteger)account.Balance;
        }

        public async Task<Result<Account?>> GetAccountAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"accounts/{address}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    headers: GetHeaders(),
                    requestLimitControl: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            return JsonSerializer.Deserialize<Account>(content);
        }

        public async Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var michelineFormat = MichelineFormat.Json;

            var requestUri = $"operations/{txId}?micheline={(int)michelineFormat}";

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    headers: GetHeaders(),
                    requestLimitControl: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var operations = JsonSerializer.Deserialize<IEnumerable<Operation>>(content);

            if (operations == null || !operations.Any())
                return new Error(Errors.GetTransactionError, "Operations is null or empty");

            return new TezosOperation(operations, michelineFormat);
        }

        [Obsolete("Use get operations by address")]
        public async Task<Result<IEnumerable<TezosOperation>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default)
        {
            const int limit = 1000;
            var received = limit;
            var lastId = 0L;

            var accountOperations = new List<Operation>();

            while (received == limit)
            {
                var requestUri = $"operations/transactions?" +
                    $"sender={from}" +
                    $"&target={to}" +
                    $"&{parameters}" +
                    (lastId != 0 ? $"&lastId={lastId}" : "");

                using var response = await HttpHelper.GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    headers: GetHeaders(),
                    requestLimitControl: null,
                    cancellationToken: cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                var operations = JsonSerializer.Deserialize<IEnumerable<Operation>>(content);

                received = operations.Count();

                if (received > 0)
                {
                    accountOperations.AddRange(operations);
                    lastId = operations.LastOrDefault()?.Id ?? 0;
                }
            };

            return new Result<IEnumerable<TezosOperation>>
            {
                Value = accountOperations
                    .GroupBy(o => o.Hash)
                    .Select((og) => new TezosOperation(og, MichelineFormat.Json))
            };
        }

        public Task<Result<IEnumerable<TezosOperation>>> GetOperationsByAddressAsync(
            string address,
            DateTimeOffset? fromTimeStamp = null,
            CancellationToken cancellationToken = default)
        {
            return GetOperationsByAddressAsync(
                address: address,
                fromTimeStamp: fromTimeStamp,
                filter: null,
                michelineFormat: MichelineFormat.Json,
                cancellationToken: cancellationToken);
        }

        public async Task<Result<IEnumerable<TezosOperation>>> GetOperationsByAddressAsync(
            string address,
            DateTimeOffset? fromTimeStamp = null,
            string? filter = null,
            MichelineFormat michelineFormat = MichelineFormat.Json,
            CancellationToken cancellationToken = default)
        {
            const int limit = 1000;
            var received = limit;
            var lastId = 0L;

            var accountOperations = new List<Operation>();

            while (received == limit)
            {
                var requestUri = $"accounts/{address}/operations?" +
                    (fromTimeStamp != null ? $"timestamp.ge={fromTimeStamp.Value.ToIso8601()}" : "") +
                    $"&limit={limit}" +
                    $"&micheline={(int)michelineFormat}" +
                    (filter != null ? $"&{filter}" : "") +
                    (lastId != 0 ? $"&lastId={lastId}" : "");

                using var response = await HttpHelper.GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    headers: GetHeaders(),
                    requestLimitControl: null,
                    cancellationToken: cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                var operations = JsonSerializer.Deserialize<IEnumerable<Operation>>(content);

                received = operations.Count();

                if (received > 0)
                {
                    accountOperations.AddRange(operations);
                    lastId = operations.LastOrDefault()?.Id ?? 0;
                }
            };

            return new Result<IEnumerable<TezosOperation>>
            {
                Value = accountOperations
                    .GroupBy(o => o.Hash)
                    .Select((og) => new TezosOperation(og, michelineFormat))
            };
        }

        public async Task<Result<bool>> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (account == null)
                return false;

            return account.Revealed;
        }

        public async Task<Result<int>> GetCounterAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (account == null)
                return 0;

            return account.Counter;
        }

        public async Task<Result<string>> GetHeaderAsync(
            CancellationToken cancellationToken = default)
        {
            using var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: $"head",
                    headers: GetHeaders(),
                    requestLimitControl: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            var hash = JsonSerializer.Deserialize<JsonElement>(content)
                .GetProperty("hash")
                .GetString();

            if (hash == null)
                return new Error(Errors.GetHeaderError, "Get header error");

            return hash;
        }

        private HttpRequestHeaders? GetHeaders() => Settings.Headers != null
            ? new HttpRequestHeaders(Settings.Headers)
            : null;

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

                using var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        headers: GetHeaders(),
                        requestLimitControl: null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return new Error((int)response.StatusCode, "Error status code received");

                var content = await response
                    .Content
                    .ReadAsStringAsync()
                    .ConfigureAwait(false);

                var contentObject = JsonSerializer.Deserialize<JsonElement>(content);

                if (!contentObject.TryGetProperty("active", out var activeProperty))
                    return false;

                return activeProperty.GetBoolean();
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        //public override async Task<Result<string>> BroadcastAsync(
        //    ITransaction transaction,
        //    CancellationToken cancellationToken = default)
        //{
        //    var tx = (TezosOperation)transaction;

        //    try
        //    {
        //        tx.Status = TransactionStatus.Pending;

        //        var rpc = new Rpc(_rpcNodeUri);
        //        var readyForInject = true;

        //        if (tx.UsePreApply)
        //        {
        //            var opResults = await rpc
        //                .PreApplyOperations(tx.Head, tx.Operations, tx.SignedMessage.EncodedSignature)
        //                .ConfigureAwait(false);

        //            if (!opResults.Any())
        //                return new Error(Errors.EmptyPreApplyOperations, "Empty pre apply operations");

        //            foreach (var opResult in opResults)
        //                Log.Debug("OperationResult {@result}: {@opResult}", opResult.Succeeded, opResult.Data.ToString());

        //            readyForInject = opResults.Any() && opResults.All(op => op.Succeeded);
        //        }

        //        string txId = null;

        //        if (readyForInject)
        //        {
        //            var injectedOperation = await rpc
        //                .InjectOperations(tx.SignedMessage.SignedBytes)
        //                .ConfigureAwait(false);

        //            if (injectedOperation != null)
        //                Log.Debug($"Injection result: {injectedOperation}");

        //            txId = injectedOperation.ToString();
        //        }

        //        if (txId == null)
        //        {
        //            tx.RollbackOfflineCounterIfNeed();

        //            return new Error(Errors.NullTxId, "Null tx id");
        //        }

        //        tx.Id = txId;

        //        return tx.Id;
        //    }
        //    catch (Exception e)
        //    {
        //        Log.Error(e, $"Broadcast error: {e.Message}");

        //        tx.RollbackOfflineCounterIfNeed();

        //        return new Error(Errors.RequestError, e.Message);
        //    }
        //}

        #region ITokenBlockchainApi

        //public async Task<Result<decimal>> GetFa12AllowanceAsync(
        //    string holderAddress,
        //    string spenderAddress,
        //    string callingAddress,
        //    SecureBytes securePublicKey,
        //    CancellationToken cancellationToken = default)
        //{
        //    var tokenConfig = _currency as Fa12Config;

        //    try
        //    {
        //        var rpc = new Rpc(_rpcNodeUri);

        //        var tx = new TezosOperation
        //        {
        //            Currency          = tokenConfig.Name,
        //            From              = callingAddress,
        //            To                = tokenConfig.TokenContractAddress,
        //            Fee               = 0, //token.GetAllowanceFee,
        //            GasLimit          = tokenConfig.GetAllowanceGasLimit,
        //            StorageLimit      = 0, //token.GetAllowanceStorageLimit,
        //            Params            = CreateGetAllowanceParams(holderAddress, spenderAddress, tokenConfig.ViewContractAddress),

        //            UseRun            = false,
        //            UseOfflineCounter = false
        //        };

        //        _ = await tx
        //            .FillOperationsAsync(
        //                securePublicKey: securePublicKey,
        //                tezosConfig: tokenConfig,
        //                cancellationToken: cancellationToken)
        //            .ConfigureAwait(false);

        //        var runResults = await rpc
        //            .RunOperations(tx.Head, tx.Operations)
        //            .ConfigureAwait(false);

        //        return runResults
        //            ?["contents"]
        //            ?.LastOrDefault()
        //            ?["metadata"]
        //            ?["internal_operation_results"]
        //            ?[0]
        //            ?["result"]
        //            ?["errors"]
        //            ?[1]
        //            ?["with"]
        //            ?["args"]
        //            ?[0]
        //            ?["args"]
        //            ?[0]
        //            ?["int"]
        //            ?.Value<decimal>() ?? 0;
        //    }
        //    catch (Exception e)
        //    {
        //        return new Error(Errors.RequestError, e.Message);
        //    }
        //}

        #endregion

        //private JObject CreateGetAllowanceParams(
        //    string holderAddress,
        //    string spenderAddress,
        //    string viewContractAddress)
        //{
        //    return JObject.FromObject(new
        //    {
        //        entrypoint = "getAllowance",
        //        value = new
        //        {
        //            args = new object[]
        //            {
        //                new
        //                {
        //                    args = new object[]
        //                    {
        //                        new
        //                        {
        //                            @string = holderAddress
        //                        },
        //                        new
        //                        {
        //                            @string = spenderAddress
        //                        }
        //                    },
        //                    prim = "Pair"
        //                },
        //                new
        //                {
        //                    @string = viewContractAddress + "%viewNat"
        //                }
        //            },
        //            prim = "Pair"
        //        }
        //    });
        //}

        //public async Task<Result<List<TokenBalance>>> GetTokenBalanceAsync(
        //    IEnumerable<string> addresses,
        //    IEnumerable<string> tokenContracts = null,
        //    IEnumerable<int> tokenIds = null,
        //    int offset = 0,
        //    int limit = PageSize,
        //    CancellationToken cancellationToken = default)
        //{
        //    var hasPages = true;
        //    var result = new List<TokenBalance>();

        //    var accountsFilter = addresses.Count() == 1
        //        ? $"account={addresses.First()}"
        //        : $"account.in={string.Join(',', addresses)}";

        //    var tokenContractsFilter = tokenContracts != null
        //        ? tokenContracts.Count() == 1
        //            ? $"&token.contract={tokenContracts.First()}"
        //            : $"&token.contract.in={string.Join(',', tokenContracts)}"
        //        : "";

        //    var tokenIdsFilter = tokenIds != null
        //        ? tokenIds.Count() == 1
        //            ? $"&token.tokenId={tokenIds.First()}"
        //            : $"&token.tokenId.in={string.Join(',', tokenIds)}"
        //        : "";

        //    while (hasPages)
        //    {
        //        var requestUri = $"tokens/balances?" +
        //            accountsFilter +
        //            tokenContractsFilter +
        //            tokenIdsFilter +
        //            $"&offset={offset}" +
        //            $"&limit={limit}";

        //        var res = await HttpHelper
        //            .GetAsyncResult<List<TokenBalanceResponse>>(
        //                baseUri: _baseUri,
        //                requestUri: requestUri,
        //                responseHandler: (response, content) => JsonConvert.DeserializeObject<List<TokenBalanceResponse>>(content),
        //                cancellationToken: cancellationToken)
        //            .ConfigureAwait(false);

        //        if (res.HasError)
        //            return res.Error;

        //        if (res.Value.Any())
        //        {
        //            result.AddRange(res.Value.Select(x => x.ToTokenBalance()));
        //            offset += res.Value.Count;

        //            if (res.Value.Count < PageSize)
        //                hasPages = false;
        //        }
        //        else
        //        {
        //            hasPages = false;
        //        }
        //    }

        //    return result;
        //}

        //public async Task<Result<List<TokenTransfer>>> GetTokenTransfersAsync(
        //    IEnumerable<string> addresses,
        //    IEnumerable<string> tokenContracts = null,
        //    IEnumerable<int> tokenIds = null,
        //    DateTimeOffset? from = null,
        //    DateTimeOffset? to = null,
        //    int offset = 0,
        //    int limit = PageSize,
        //    CancellationToken cancellationToken = default)
        //{
        //    var tokenContractsFilter = tokenContracts != null
        //        ? tokenContracts.Count() == 1
        //            ? $"&token.contract={tokenContracts.First()}"
        //            : $"&token.contract.in={string.Join(',', tokenContracts)}"
        //        : "";

        //    var tokenIdsFilter = tokenIds != null
        //        ? tokenIds.Count() == 1
        //            ? $"&token.tokenId={tokenIds.First()}"
        //            : $"&token.tokenId.in={string.Join(',', tokenIds)}"
        //        : "";

        //    var fromTimeStampFilter = from != null
        //        ? $"&timestamp.gt={from.Value.ToUtcIso8601()}"
        //        : "";

        //    var toTimeStampFilter = to != null
        //        ? $"&timestamp.le={to.Value.ToUtcIso8601()}"
        //        : "";

        //    // todo: use `anyof.from.to.in` after release in TzKT
        //    var accountsFilters = addresses.Count() == 1
        //        ? new string[] {
        //            $"anyof.from.to={addresses.First()}"
        //        }
        //        : new string[] {
        //            $"from.in={string.Join(',', addresses)}",
        //            $"to.in={string.Join(',', addresses)}"
        //        };

        //    var transfers = new List<TokenTransfer>();

        //    // todo: use `anyof.from.to.in` after release in TzKT
        //    foreach (var accountFilter in accountsFilters)
        //    {
        //        var hasPages = true;
        //        var transfersCount = 0;

        //        while (hasPages && transfersCount < limit)
        //        {
        //            var requestLimit = Math.Min(limit - transfersCount, PageSize);

        //            var requestUri = "tokens/transfers?" +
        //                accountFilter +
        //                tokenContractsFilter +
        //                tokenIdsFilter +
        //                fromTimeStampFilter +
        //                toTimeStampFilter +
        //                $"&offset={offset}" +
        //                $"&limit={requestLimit}";

        //            var tokenTransfersRes = await HttpHelper
        //                .GetAsyncResult<List<TokenTransferResponse>>(
        //                    baseUri: _baseUri,
        //                    requestUri: requestUri,
        //                    responseHandler: (_, content) => JsonConvert.DeserializeObject<List<TokenTransferResponse>>(content),
        //                    cancellationToken: cancellationToken)
        //                .ConfigureAwait(false);

        //            if (tokenTransfersRes.HasError)
        //                return tokenTransfersRes.Error;

        //            if (tokenTransfersRes.Value.Any())
        //            {
        //                // hint: don't increase value of this variable
        //                // Otherwise TzKT API can response with 414 Request-URI Too Large
        //                const int MaxOperationIdsPerRequest = 100;

        //                var uniqueOperationIds = tokenTransfersRes.Value
        //                    .Select(tokenTransfer => tokenTransfer.TransactionId)
        //                    .Distinct()
        //                    .ToList();

        //                var operations = new List<TokenOperation>();

        //                var operationsIdsGroupsCount = Math.Ceiling(uniqueOperationIds.Count / (decimal)MaxOperationIdsPerRequest);

        //                for (var i = 0; i < operationsIdsGroupsCount; i++)
        //                {
        //                    var operationIdsGroup = uniqueOperationIds
        //                        .Skip(i * MaxOperationIdsPerRequest)
        //                        .Take(MaxOperationIdsPerRequest);

        //                    var operationIdsGroupString = string.Join(',', operationIdsGroup);

        //                    var tokenOperationsRes = await HttpHelper
        //                        .GetAsyncResult<List<TokenOperation>>(
        //                            baseUri: _baseUri,
        //                            requestUri: $"operations/transactions?id.in={operationIdsGroupString}&select=hash,counter,nonce,id&limit={MaxOperationIdsPerRequest}",
        //                            responseHandler: (_, content) => JsonConvert.DeserializeObject<List<TokenOperation>>(content),
        //                            cancellationToken: cancellationToken)
        //                        .ConfigureAwait(false);

        //                    if (tokenOperationsRes.HasError)
        //                        return tokenOperationsRes.Error;

        //                    operations.AddRange(tokenOperationsRes.Value);
        //                }

        //                transfers.AddRange(tokenTransfersRes.Value.Select(tokenTransfer =>
        //                    {
        //                        var tokenOperation = operations
        //                            .Find(to => to.Id == tokenTransfer.TransactionId);

        //                        return tokenTransfer.ToTokenTransfer(
        //                            tokenOperation?.Hash ?? string.Empty,
        //                            tokenOperation?.Counter ?? 0,
        //                            tokenOperation?.Nonce
        //                        );
        //                    })
        //                );

        //                transfersCount += tokenTransfersRes.Value.Count;
        //                offset += tokenTransfersRes.Value.Count;

        //                if (tokenTransfersRes.Value.Count < limit)
        //                    hasPages = false;
        //            }
        //            else
        //            {
        //                hasPages = false;
        //            }
        //        }
        //    }

        //    return transfers
        //        .Distinct(new Common.EqualityComparer<TokenTransfer>(
        //            (t1,t2) => t1.Id.Equals(t2.Id),
        //            t => t.Id.GetHashCode()))
        //        .ToList();
        //}
    }
}