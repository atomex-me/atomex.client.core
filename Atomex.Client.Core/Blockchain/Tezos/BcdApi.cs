﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.Tezos.Bcd
{
    public class TokenContract
    {
        public string Id => Address;

        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("network")]
        public string Network { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("homepage")]
        public string HomePage { get; set; }
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("authors")]
        public List<string> Authors { get; set; }
        [JsonPropertyName("interfaces")]
        public List<string> Interfaces { get; set; }
        [JsonPropertyName("contract_tags")]
        public List<string> ContractTags { get; set; }

        public string GetContractType()
        {
            if (ContractTags != null)
            {
                if (ContractTags.Contains("fa2"))
                    return "FA2";

                if (ContractTags.Contains("fa1-2"))
                    return "FA12";
            }

            if (Interfaces == null)
                return "FA2";

            if (Interfaces.FirstOrDefault(i => i == "TZIP-12" || i == "TZIP-012" || i.StartsWith("TZIP-012")) != null)
                return "FA2";

            if (Interfaces.FirstOrDefault(i => i == "TZIP-7" || i == "TZIP-007" || i.StartsWith("TZIP-007")) != null)
                return "FA12";

            return "FA2";
        }
    }

    public class TokenContractWithMetadata : TokenContract
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class TokenContractResponse : Dictionary<string, TokenContractWithMetadata>
    {
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
            Balance.TryParseWithRound(Decimals, out var result)
                ? result
                : 0;

        public bool HasDescription =>
            !string.IsNullOrEmpty(Description);
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

    public class TokenTransfer : IBlockchainTransaction
    {
        public string Id => $"{Hash}:{Nonce}";
        public string Currency { get; set; }
        public BlockInfo BlockInfo => new()
        {
            BlockHash     = null,
            BlockHeight   = Level,
            BlockTime     = TimeStamp.UtcDateTime,
            Confirmations = 1,
            FirstSeen     = TimeStamp.UtcDateTime
        };
        public BlockchainTransactionState State { get; set; } = BlockchainTransactionState.Confirmed;
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime => TimeStamp.UtcDateTime;
        public bool IsConfirmed => true;

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
        [JsonPropertyName("token")]
        public Token Token { get; set; }
        [JsonPropertyName("alias")]
        public string Alias { get; set; }
        [JsonPropertyName("initiator_alias")]
        public string InitiatorAlias { get; set; }
        [JsonPropertyName("to_alias")]
        public string ToAlias { get; set; }
        [JsonPropertyName("entrypoint")]
        public string Entrypoint { get; set; }
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
        public int MaxTokensSize { get; set; }
        public int MaxTokensPerUpdate { get; set; }
        public int MaxTransfersPerUpdate { get; set; }
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
            var requestUri = $"account/{_config.Network}/{address}/count_with_metadata";

            return HttpHelper.GetAsyncResult<TokenContractResponse>(
                baseUri: _config.Uri,
                requestUri: requestUri,
                responseHandler: (response, content) => JsonSerializer.Deserialize<TokenContractResponse>(content),
                cancellationToken: cancellationToken);
        }


        public async Task<Result<List<TokenBalance>>> GetTokenBalancesAsync(
            string address,
            string contractAddress = null,
            int offset = 0,
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            var hasPages = true;

            var tokenBalances = new List<TokenBalance>();

            while (hasPages && tokenBalances.Count < count)
            {
                var size = Math.Min(count - tokenBalances.Count, _config.MaxTokensSize);

                var requestUri = $"account/{_config.Network}/{address}/token_balances?" +
                    $"size={size}" +
                    $"&offset={offset}" +
                    (contractAddress != null ? $"&contract={contractAddress}" : "");

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

                offset += size;
            }

            return tokenBalances;
        }

        public Task<Result<List<TokenTransfer>>> GetTokenTransfers(
            string address,
            string contract,
            decimal? tokenId = null,
            int start = 0,
            int end = int.MaxValue,
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            return GetTokenTransfers(
                address: address,
                contracts: new string[] { contract },
                tokenId: tokenId,
                start: start,
                end: end,
                count: count,
                cancellationToken: cancellationToken);
        }

        public async Task<Result<List<TokenTransfer>>> GetTokenTransfers(
            string address,
            IEnumerable<string> contracts,
            decimal? tokenId = null,
            int start = 0,
            int end = int.MaxValue,
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            var hasPages = true;
            string lastId = null;

            var tokenTransfers = new List<TokenTransfer>();

            while (hasPages && tokenTransfers.Count < count)
            {
                var size = Math.Min(count - tokenTransfers.Count, _config.MaxSize);

                var requestUri = $"tokens/{_config.Network}/transfers/{address}?" +
                    $"size={size}" +
                    $"&contracts={string.Join(",", contracts)}" +
                    (tokenId != null    ? $"&token_id={tokenId}" : "") +
                    (start > 0          ? $"&start={start}"      : "") +
                    (end < int.MaxValue ? $"&end={end}"          : "") +
                    (lastId != null     ? $"&last_id={lastId}"   : "");

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