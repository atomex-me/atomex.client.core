using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.Tezos
{
    public class TokenContractWithMetadata
    {
        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("network")]
        public string Network { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("homepage")]
        public string HomePage { get; set; }
        [JsonPropertyName("interfaces")]
        public List<string> Interfaces { get; set; }
        [JsonPropertyName("count")]
        public int Count { get; set; }

        public string GetContractType()
        {
            if (Interfaces == null)
                return "";

            if (Interfaces.FirstOrDefault(i => i.StartsWith("TZIP-007")) != null)
                return "FA12";

            if (Interfaces.FirstOrDefault(i => i.StartsWith("TZIP-012")) != null)
                return "FA2";

            return "";
        }
    }

    public class TokenContractResponse : Dictionary<string, TokenContractWithMetadata>
    {
        public string GetContractType(string contractAddress) =>
            TryGetValue(contractAddress, out var tokenContract)
                ? tokenContract.GetContractType()
                : "";
    }

    public class TokenBalance
    {
        [JsonPropertyName("contract")]
        public string Contract { get; set; }
        [JsonPropertyName("network")]
        public string Network { get; set; }
        [JsonPropertyName("token_id")]
        public decimal TokenId { get; set; }
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("decimals")]
        public int Decimals { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("artifact_uri")]
        public string ArtifactUri { get; set; }
        [JsonPropertyName("display_uri")]
        public string DisplayUri { get; set; }
        [JsonPropertyName("thumbnail_uri")]
        public string ThumbnailUri { get; set; }
        [JsonPropertyName("is_transferable")]
        public bool IsTransferable { get; set; }
        [JsonPropertyName("creators")]
        public List<string> Creators { get; set; }

        [JsonPropertyName("balance")]
        public string Balance { get; set; }

        public decimal GetTokenBalance() =>
            decimal.Parse(Balance) / (decimal)BigInteger.Pow(10, Decimals);
    }

    public class TokenBalanceResponse
    {
        [JsonPropertyName("balances")]
        public List<TokenBalance> Balances { get; set; }
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    public class Token
    {
        [JsonPropertyName("contract")]
        public string Contract { get; set; }
        [JsonPropertyName("network")]
        public string Network { get; set; }
        [JsonPropertyName("token_id")]
        public decimal TokenId { get; set; }
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("decimals")]
        public int Decimals { get; set; }
    }

    public class TokenTransfer
    {
        public string Id => $"{Hash}:{Nonce}";

        [JsonPropertyName("indexed_time")]
        public int IndexedTime { get; set; }
        [JsonPropertyName("network")]
        public string Network { get; set; }
        [JsonPropertyName("contract")]
        public string Contract { get; set; }
        [JsonPropertyName("initiator")]
        public string Initiator { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("timestamp")]
        public DateTimeOffset TimeStamp { get; set; }
        [JsonPropertyName("level")]
        public int Level { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("token_id")]
        public decimal TokenId { get; set; }
        [JsonPropertyName("amount")]
        public string Amount { get; set; }
        [JsonPropertyName("counter")]
        public int Counter { get; set; }
        [JsonPropertyName("nonce")]
        public int Nonce { get; set; }
        [JsonPropertyName("alias")]
        public string Alias { get; set; }
        [JsonPropertyName("initiator_alias")]
        public string InitiatorAlias { get; set; }
        [JsonPropertyName("to_alias")]
        public string ToAlias { get; set; }
    }


    public class TokenTransferResponse
    {
        [JsonPropertyName("transfers")]
        public List<TokenTransfer> Transfers { get; set; }
        [JsonPropertyName("total")]
        public int Total { get; set; }
        [JsonPropertyName("last_id")]
        public string LastId { get; set; } 
    }

    public class BcdApiSettings
    {
        public string Uri { get; set; }
        public string Network { get; set; }
        public int MaxSize { get; set; }
    }

    public class BcdApi
    {
        private readonly BcdApiSettings _config;

        public BcdApi(BcdApiSettings bcdConfig)
        {
            _config = bcdConfig ?? throw new ArgumentNullException(nameof(bcdConfig));
        }

        public Task<Result<TokenContractResponse>> GetTokenContractsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"/v1/account/{_config.Network}/{address}/count_with_metadata";

            return HttpHelper.GetAsyncResult<TokenContractResponse>(
                baseUri: _config.Uri,
                requestUri: requestUri,
                responseHandler: (response, content) => JsonSerializer.Deserialize<TokenContractResponse>(content),
                cancellationToken: cancellationToken);
        }

        public async Task<Result<List<TokenBalance>>> GetTokenBalancesAsync(
            string address,
            string contractAddress,
            CancellationToken cancellationToken = default)
        {
            int offset = 0;
            int size = _config.MaxSize;
            var hasPages = true;

            var tokenBalances = new List<TokenBalance>();

            while (hasPages)
            {
                var requestUri = $"/v1/account/{_config.Network}/{address}/token_balances?" +
                    $"contract={contractAddress}&" +
                    $"size={size}&" +
                    $"offset={offset}";

                var balances = await HttpHelper
                    .GetAsyncResult<TokenBalanceResponse>(
                        baseUri: _config.Uri,
                        requestUri: requestUri,
                        responseHandler: (response, content) => JsonSerializer.Deserialize<TokenBalanceResponse>(content),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (balances.HasError)
                    return balances.Error;

                if (!balances.Value.Balances.Any())
                {
                    if (balances.Value.Total != tokenBalances.Count)
                        return new Error(Errors.InvalidResponse, "Fewer token balance entries received than expected.");

                    return tokenBalances;
                }

                tokenBalances.AddRange(balances.Value.Balances);

                hasPages = tokenBalances.Count < balances.Value.Total;
                    
                offset++;
            }

            return tokenBalances;
        }

        public async Task<Result<List<TokenTransfer>>> GetTokenTransfers(
            string address,
            string contractAddress,
            decimal tokenId,
            CancellationToken cancellationToken = default)
        {
            int size = _config.MaxSize;
            var hasPages = true;
            string lastId = null;

            var tokenTransfers = new List<TokenTransfer>();

            while (hasPages)
            {
                var requestUri = $"v1/tokens/{_config.Network}/transfers/{address}?" +
                    $"token_id={tokenId}" +
                    $"&size={size}" +
                    "&offset=0" +
                    $"&contracts={contractAddress}" +
                    (lastId != null ? $"&last_id={lastId}" : "");

                var transfers = await HttpHelper
                    .GetAsyncResult<TokenTransferResponse>(
                        baseUri: _config.Uri,
                        requestUri: requestUri,
                        responseHandler: (response, content) => JsonSerializer.Deserialize<TokenTransferResponse>(content),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (transfers.HasError)
                    return transfers.Error;

                if (!transfers.Value.Transfers.Any())
                    return tokenTransfers;

                tokenTransfers.AddRange(transfers.Value.Transfers);

                hasPages = transfers.Value.Total > transfers.Value.Transfers.Count;

                lastId = transfers.Value.Transfers
                    .Last()
                    .IndexedTime
                    .ToString();
            }

            return tokenTransfers;
        }
    }
}