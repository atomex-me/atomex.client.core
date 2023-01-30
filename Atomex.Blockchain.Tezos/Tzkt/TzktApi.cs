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
using Atomex.Blockchain.Tezos.Tzkt.Operations;

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
        public const int TokenBalanceLimit = 10000;
        public const int TokenTransfersLimit = 10000;

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

        public Task<Result<IEnumerable<TezosOperation>>> GetOperationsByAddressAsync(
            string address,
            DateTimeParameter? timeStamp = null,
            CancellationToken cancellationToken = default)
        {
            return GetOperationsByAddressAsync(
                address: address,
                timeStamp: timeStamp,
                michelineFormat: MichelineFormat.Json,
                cancellationToken: cancellationToken);
        }

        public async Task<Result<IEnumerable<TezosOperation>>> GetOperationsByAddressAsync(
            string address,
            DateTimeParameter? timeStamp = null,
            string? type = null,
            string? entrypoint = null,
            string? parameter = null,
            MichelineFormat michelineFormat = MichelineFormat.Json,
            CancellationToken cancellationToken = default)
        {
            const int LimitPerRequest = 1000;
            var receivedByRequest = 0;
            var lastId = 0L;

            var accountOperations = new List<Operation>();

            do
            {
                var requestUri = $"accounts/{address}/operations?" +
                    (timeStamp != null ? timeStamp.Value.ToString("timestamp", d => d.ToIso8601()) : "") +
                    $"&limit={LimitPerRequest}" +
                    $"&micheline={(int)michelineFormat}" +
                    (type != null ? $"&type={type}" : "") +
                    (entrypoint != null ? $"&entrypoint={entrypoint}" : "") +
                    (parameter != null ? $"&{parameter}" : "") +
                    (lastId != 0 ? $"&lastId={lastId}" : "");

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

                receivedByRequest = operations.Count();

                if (receivedByRequest > 0)
                {
                    accountOperations.AddRange(operations);
                    lastId = operations.LastOrDefault()?.Id ?? 0;
                }
            }
            while (receivedByRequest == LimitPerRequest);

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

        public async Task<Result<bool>> IsAllocatedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (account == null)
                return false;

            if (account.Type == "empty")
                return false;

            if (account.Type == "user")
                return account.Balance > 0;

            return true;
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

        public async Task<Result<List<TokenBalance>>> GetTokenBalanceAsync(
            IEnumerable<string> addresses,
            IEnumerable<string>? tokenContracts = null,
            IEnumerable<int>? tokenIds = null,
            int offset = 0,
            int limit = int.MaxValue,
            CancellationToken cancellationToken = default)
        {
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

            while (true)
            {
                var requestUri = $"tokens/balances?" +
                    accountsFilter +
                    tokenContractsFilter +
                    tokenIdsFilter +
                    $"&offset={offset}" +
                    $"&limit={Math.Min(limit, TokenBalanceLimit)}";

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

                var tokenBalanceResponse = JsonSerializer.Deserialize<List<TokenBalanceResponse>>(content);

                if (tokenBalanceResponse == null || !tokenBalanceResponse.Any())
                    break; // no more pages

                result.AddRange(tokenBalanceResponse.Select(r => r.ToTokenBalance()));

                limit -= tokenBalanceResponse.Count;
                offset += tokenBalanceResponse.Count;

                if (limit <= 0)
                    break; // completed
            }

            return result;
        }

        public async Task<Result<List<TezosTokenTransfer>>> GetTokenTransfersAsync(
            IEnumerable<string> addresses,
            IEnumerable<string>? tokenContracts = null,
            IEnumerable<int>? tokenIds = null,
            DateTimeParameter? timeStamp = null,
            int offset = 0,
            int limit = int.MaxValue,
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

            var timeStampFilter = timeStamp != null
                ? timeStamp.Value.ToString("timestamp", d => d.ToUtcIso8601())
                : "";

            var accountsFilter = addresses.Count() == 1
                ? $"anyof.from.to={addresses.First()}"
                : $"anyof.from.to.in={string.Join(',', addresses)}";

            var transfers = new List<TezosTokenTransfer>();

            while (true)
            {
                var requestUri = "tokens/transfers?" +
                    accountsFilter +
                    tokenContractsFilter +
                    tokenIdsFilter +
                    timeStampFilter +
                    $"&offset={offset}" +
                    $"&limit={Math.Min(limit, TokenTransfersLimit)}";

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

                var tokenTransfersResponse = JsonSerializer.Deserialize<List<TokenTransferResponse>>(content);

                if (tokenTransfersResponse == null || !tokenTransfersResponse.Any())
                    break; // no more pages

                // hint: don't increase value of this variable
                // Otherwise TzKT API can response with 414 Request-URI Too Large
                const int MaxOperationIdsPerRequest = 100;

                var uniqueOperationIds = tokenTransfersResponse
                    .Select(tokenTransfer => tokenTransfer.TransactionId)
                    .Distinct()
                    .ToList();

                var operations = new List<TokenOperation>();
                var operationsIdsGroupsCount = Math.Ceiling(uniqueOperationIds.Count / (decimal)MaxOperationIdsPerRequest);

                for (var i = 0; i < operationsIdsGroupsCount; i++)
                {
                    var operationIdsGroup = uniqueOperationIds
                        .Skip(i * MaxOperationIdsPerRequest)
                        .Take(MaxOperationIdsPerRequest);

                    var operationIdsGroupString = string.Join(',', operationIdsGroup);

                    var operationRequestUri = $"operations/transactions?" +
                        $"id.in={operationIdsGroupString}" +
                        $"&select=hash,counter,nonce,id" +
                        $"&limit={MaxOperationIdsPerRequest}";

                    using var operationResponse = await HttpHelper
                        .GetAsync(
                            baseUri: Settings.BaseUri,
                            relativeUri: requestUri,
                            headers: GetHeaders(),
                            requestLimitControl: null,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (!operationResponse.IsSuccessStatusCode)
                        return new Error((int)operationResponse.StatusCode, "Error status code received");

                    content = await operationResponse
                        .Content
                        .ReadAsStringAsync()
                        .ConfigureAwait(false);

                    var tokenOperationsResponse = JsonSerializer.Deserialize<List<TokenOperation>>(content);

                    if (tokenOperationsResponse != null && tokenOperationsResponse.Any())
                        operations.AddRange(tokenOperationsResponse);
                }

                transfers.AddRange(tokenTransfersResponse.Select(t =>
                {
                    var tokenOperation = operations
                        .Find(to => to.Id == t.TransactionId);

                    return t.ToTokenTransfer(
                        tokenOperation?.Hash ?? string.Empty,
                        tokenOperation?.Counter ?? 0,
                        tokenOperation?.Nonce
                    );
                }));

                limit -= tokenTransfersResponse.Count;
                offset += tokenTransfersResponse.Count;

                if (limit <= 0)
                    break; // completed
            }

            return transfers
                .Distinct(new Atomex.Common.EqualityComparer<TezosTokenTransfer>(
                    (t1, t2) => t1.Id.Equals(t2.Id),
                    t => t.Id.GetHashCode()))
                .ToList();
        }
    }
}