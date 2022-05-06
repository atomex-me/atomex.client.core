using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Abstract;
using Atomex.Blockchain.Tezos.Common;
using Atomex.Blockchain.Tezos.Operations;
using Atomex.Blockchain.Tezos.Tzkt.Swaps.V1;
using Atomex.Common;

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
        public List<TzktTokenContractSettings> TokenContracts { get; set; }

        public string GetTokenContract(string token) =>
            TokenContracts?.FirstOrDefault(s => s.Token == token)?.Address;
    }

    public class TzktApi : ITezosApi, IBlockchainSwapApi
    {
        public const string Uri = "https://api.tzkt.io/v1/";
        public const int PageSize = 10000;

        public TzktSettings Settings { get; set; }

        public TzktApi(TzktSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #region IBlockchainApi

        public async Task<(decimal balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (balance: 0, error);

            return (balance: account.Balance.ToTez(), error: null);
        }

        public async Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var micheline = MichelineFormat.RawMichelineString;

            var requestUri = $"operations/{txId}?micheline={(int)micheline}";

            var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var error = !response.IsSuccessStatusCode
                ? new Error((int)response.StatusCode, "Error status code received")
                : null;

            if (error != null)
                return (tx: null, error);

            var content = await response
                .Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var tx = response.IsSuccessStatusCode
                ? new TezosOperation(JsonSerializer.Deserialize<IEnumerable<Operation>>(content))
                : null;

            return (tx, error: null);
        }

        public Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<(string txId, Error error)>(
                (txId: null,
                 error: new Error(
                    code: Errors.NotSupportedError,
                    description: "tzkt.io not supported operations broadcast. Please use node rpc call instead"))
            );
        }

        #endregion IBlockchainApi

        #region ITezosApi

        public Task<(IEnumerable<TezosOperation> ops, Error error)> GetOperationsAsync(
            string address,
            DateTimeOffset fromTimeStamp,
            CancellationToken cancellationToken = default)
        {
            return GetOperationsAsync(
                address: address,
                fromTimeStamp: fromTimeStamp,
                filter: null,
                michelineFormat: MichelineFormat.RawMichelineString,
                cancellationToken: cancellationToken);
        }

        public async Task<(TezosAccount account, Error error)> GetAccountAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"accounts/{address}";

            var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var error = !response.IsSuccessStatusCode
                ? new Error((int)response.StatusCode, "Error status code received")
                : null;

            if (error != null)
                return (account: null, error);

            var content = await response
                .Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var account = response.IsSuccessStatusCode
                ? JsonSerializer.Deserialize<TezosAccount>(content)
                : null;

            return (account, error: null);
        }

        public async Task<(string hash, Error error)> GetHeaderAsync(
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            var response = await HttpHelper
                .GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: $"head",
                    requestLimitControl: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var error = !response.IsSuccessStatusCode
                ? new Error((int)response.StatusCode, "Error status code received")
                : null;

            if (error != null)
                return (hash: null, error);

            var content = await response
                .Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var hash = response.IsSuccessStatusCode
                ? JsonSerializer.Deserialize<JsonElement>(content)
                    .GetProperty("hash")
                    .GetString()
                : null;

            return (hash, error: null);
        }

        public async Task<(bool isRevealed, Error error)> IsRevealedAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (isRevealed: false, error);

            return (isRevealed: account.IsRevealed, error: null);
        }

        public async Task<(int? counter, Error error)> GetCounterAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (account, error) = await GetAccountAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (counter: null, error);

            return (counter: account.Counter, error: null);
        }

        public Task<(string result, Error error)> RunOperationsAsync(
            string branch,
            string chainId,
            string operations,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<(string result, Error error)>(
                (result: null,
                 error: new Error(
                    code: Errors.NotSupportedError,
                    description: "tzkt.io not supported run operations. Please use node rpc call instead"))
            );
        }

        #endregion ITezosApi

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
            var (txs, error) = await TzktSwapHelper
                .FindLocksAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
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
            var (txs, error) = await TzktSwapHelper
                .FindAdditionalLocksAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    timeStamp: timeStamp,
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
            var (txs, error) = await TzktSwapHelper
                .FindRedeemsAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    timeStamp: timeStamp,
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
            var (txs, error) = await TzktSwapHelper
                .FindRefundsAsync(
                    api: this,
                    secretHash: secretHash,
                    contractAddress: contractAddress,
                    timeStamp: timeStamp,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return (txs: null, error);

            return (txs, error: null);
        }

        #endregion IBlockchainSwapApi

        public async Task<(IEnumerable<TezosOperation> ops, Error error)> GetOperationsAsync(
            string address,
            DateTimeOffset fromTimeStamp = default,
            string filter = null,
            MichelineFormat michelineFormat = MichelineFormat.RawMichelineString,
            CancellationToken cancellationToken = default)
        {
            const int limit = 1000;
            var received = limit;
            var lastId = 0;

            var accountOperations = new List<Operation>();

            while (received == limit)
            {
                var requestUri = $"accounts/{address}/operations?" +
                    (fromTimeStamp != default ? $"timestamp.ge={fromTimeStamp.ToIso8601()}" : "") +
                    $"&limit={limit}" +
                    $"&micheline={(int)michelineFormat}" +
                    (filter != null ? $"&{filter}" : "") +
                    (lastId != 0 ? $"&lastId={lastId}" : "");

                var response = await HttpHelper.GetAsync(
                    baseUri: Settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: null,
                    cancellationToken: cancellationToken);

                var error = !response.IsSuccessStatusCode
                    ? new Error((int)response.StatusCode, "Error status code received")
                    : null;

                if (error != null)
                    return (ops: null, error);

                var content = await response
                    .Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                var operations = response.IsSuccessStatusCode
                    ? JsonSerializer.Deserialize<IEnumerable<Operation>>(content)
                    : null;

                received = operations.Count();

                if (received > 0)
                {
                    accountOperations.AddRange(operations);
                    lastId = operations.LastOrDefault()?.Id ?? 0;
                }
            };

            return (
                ops: accountOperations
                    .GroupBy(o => o.Hash)
                    .Select((og) => new TezosOperation(og)),
                error: null);
        }

        public async Task<(IEnumerable<TokenContract> contracts, Error error)> GetTokenContractsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var offset = 0;
            var hasPages = true;
            var contractAddresses = new HashSet<string>();

            while (hasPages)
            {
                var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: $"tokens/balances?account={address}&select=token.contract.address",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var error = !response.IsSuccessStatusCode
                    ? new Error((int)response.StatusCode, "Error status code received")
                    : null;

                if (error != null)
                    return (contracts: null, error);

                var content = await response
                    .Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                var addresses = response.IsSuccessStatusCode
                    ? JsonSerializer.Deserialize<List<string>>(content)
                    : null;

                if (addresses.Any())
                {
                    contractAddresses.UnionWith(addresses);
                    offset += addresses.Count;

                    if (addresses.Count < PageSize)
                        hasPages = false;
                }
                else
                {
                    hasPages = false;
                }
            }

            var contracts = new List<TokenContract>();

            foreach (var contractAddress in contractAddresses)
            {
                var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: $"contracts/{contractAddress}",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var error = !response.IsSuccessStatusCode
                    ? new Error((int)response.StatusCode, "Error status code received")
                    : null;

                if (error != null)
                    return (contracts: null, error);

                var content = await response
                    .Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                var tokenContractResponse = JsonSerializer.Deserialize<TokenContractResponse>(content);

                contracts.Add(tokenContractResponse.ToTokenContract());
            }

            return (contracts, null);
        }

        public async Task<(IEnumerable<TokenBalance> balances, Error error)> GetTokenBalancesAsync(
            string address,
            string contractAddress = null,
            CancellationToken cancellationToken = default)
        {
            var offset = 0;
            var hasPages = true;
            var tokenBalances = new List<TokenBalance>();

            while (hasPages)
            {
                var requestUri = $"tokens/balances?" +
                    $"account={address}" +
                    $"&offset={offset}" +
                    (contractAddress != null ? $"&token.contract={contractAddress}" : "");

                var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var error = !response.IsSuccessStatusCode
                    ? new Error((int)response.StatusCode, "Error status code received")
                    : null;

                if (error != null)
                    return (balances: null, error);

                var content = await response
                    .Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                var balances = JsonSerializer.Deserialize<List<TokenBalanceResponse>>(content);

                if (balances.Any())
                {
                    tokenBalances.AddRange(balances.Select(x => x.ToTokenBalance()));
                    offset += balances.Count;

                    if (balances.Count < PageSize)
                        hasPages = false;
                }
                else
                {
                    hasPages = false;
                }
            }

            return (balances: tokenBalances, error: null);
        }

        public async Task<(IEnumerable<TokenTransfer> transfers, Error error)> GetTokenTransfersAsync(
            string address,
            string contractAddress = null,
            decimal? tokenId = null,
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            var offset = 0;
            var hasPages = true;
            var transfers = new List<TokenTransfer>();

            while (hasPages && transfers.Count < count)
            {
                var limit = Math.Min(count - transfers.Count, PageSize);

                var requestUri = $"tokens/transfers?" +
                    $"anyof.from.to={address}" +
                    $"&offset={offset}" +
                    $"&limit={limit}" +
                    (contractAddress != null ? $"&token.contract={contractAddress}" : "") +
                    (tokenId != null ? $"&token.tokenId={tokenId}" : "");

                var response = await HttpHelper
                    .GetAsync(
                        baseUri: Settings.BaseUri,
                        relativeUri: requestUri,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var error = !response.IsSuccessStatusCode
                    ? new Error((int)response.StatusCode, "Error status code received")
                    : null;

                if (error != null)
                    return (transfers: null, error);

                var content = await response
                    .Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                var transfersResponses = JsonSerializer.Deserialize<List<TokenTransferResponse>>(content);

                if (transfersResponses.Any())
                {
                    transfers.AddRange(transfersResponses.Select(x => x.ToTokenTransfer()));
                    offset += transfersResponses.Count;

                    if (transfersResponses.Count < limit)
                        hasPages = false;
                }
                else
                {
                    hasPages = false;
                }
            }

            return transfers;
        }
    }
}