﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Common;

namespace Atomex.Blockchain.Tezos
{
    public enum ChainId
    {
        Main = 0,
        Test = 1
    }

    public class TezosRpcAccount
    {
        [JsonPropertyName("balance")]
        public string Balance { get; set; }
        [JsonPropertyName("delegate")]
        public string Delegate { get; set; }
        [JsonPropertyName("counter")]
        public string Counter { get; set; }
    }

    public class TezosRpcHeader
    {
        [JsonPropertyName("protocol")]
        public string Protocol { get; set; }
        [JsonPropertyName("chain_id")]
        public string ChainId { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("level")]
        public long Level { get; set; }
        [JsonPropertyName("proto")]
        public int Proto { get; set; }
        [JsonPropertyName("predecessor")]
        public string Predecessor { get; set; }
        [JsonPropertyName("timestamp")]
        public string TimeStamp { get; set; }
        [JsonPropertyName("validation_pass")]
        public int ValidationPass { get; set; }
        [JsonPropertyName("operations_hash")]
        public string OperationsHash { get; set; }
        [JsonPropertyName("fitness")]
        public string[] Fitness { get; set; }
        [JsonPropertyName("context")]
        public string Context { get; set; }
        [JsonPropertyName("payload_hash")]
        public string PayloadHash { get; set; }
        [JsonPropertyName("payload_round")]
        public int PayloadRound { get; set; }
        [JsonPropertyName("proof_of_work_nonce")]
        public string ProofOfWorkNonce { get; set; }
        [JsonPropertyName("liquidity_baking_toggle_vote")]
        public string LiquidityBakingToggleVote { get; set; }
        [JsonPropertyName("signature")]
        public string Signature { get; set; }
    }

    public class TezosRpcSettings
    {
        public string Url { get; set; }
        public string UserAgent { get; set; } = "Atomex";
        public ChainId ChainId { get; set; } = ChainId.Main;
    }

    public class TezosRpcDelegates
    {
        [JsonPropertyName("full_balance")]
        public string FullBalance { get; set; }
        [JsonPropertyName("current_frozen_deposits")]
        public string CurrentFrozenDeposits { get; set; }
        [JsonPropertyName("frozen_deposits")]
        public string FrozenDeposits { get; set; }
        [JsonPropertyName("staking_balance")]
        public string StakingBalance { get; set; }
        [JsonPropertyName("delegated_contracts")]
        public List<string> DelegatedContracts { get; set; }
        [JsonPropertyName("delegated_balance")]
        public string DelegatedBalance { get; set; }
        [JsonPropertyName("deactivated")]
        public bool Deactivated { get; set; }
        [JsonPropertyName("grace_period")]
        public long GracePeriod { get; set; }
        [JsonPropertyName("voting_power")]
        public string VotingPower { get; set; }
        [JsonPropertyName("active_consensus_key")]
        public string ActiveConsensusKey { get; set; }
    }

    public class TezosRpc
    {
        private readonly TezosRpcSettings _settings;
        private string _chainId => _settings.ChainId.ToString().ToLowerInvariant();

        public TezosRpc(TezosRpcSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public Task<string> InjectOperationsAsync(
            string signedBytesInHex,
            CancellationToken cancellationToken = default)
        {
            return QueryAsync(
                query: $"injection/operation?chain={_chainId}",
                data: $"\"{signedBytesInHex}\"",
                cancellationToken: cancellationToken);
        }

        public Task<string> RunOperationsAsync(
            string branch,
            string chainId,
            string operations,
            CancellationToken cancellationToken = default)
        {
            var contents = "{" +
                "\"operation\":{" +
                    $"\"branch\":\"{branch}\"," +
                    $"\"contents\":{operations}," +
                    $"\"signature\":\"edsigtePsnVcZ3FPzmenoU9NS1ubUsMmzSCmJgumPjUozCGLz7UwgpbPkpFP2LzC43pBS5B5tFNvDRbJ56s8by5W4Q4SrYPy6Qp\"" +
                "}," +
                $"\"chain_id\":\"{chainId}\"" +
            "}";

            return QueryAsync(
                query: $"chains/{_chainId}/blocks/head/helpers/scripts/run_operation",
                data: contents,
                cancellationToken: cancellationToken);
        }

        public Task<string> PreApplyOperationsAsync(
            string protocol,
            string branch,
            string operations,
            string signature,
            CancellationToken cancellationToken = default)
        {
            var contents = "[{" +
                $"\"protocol\":\"{protocol}\"," +
                $"\"branch\":\"{branch}\"," +
                $"\"contents\":{operations}," +
                $"\"signature\":\"{signature}\"" +
            "}]";

            return QueryAsync(
                query: $"chains/{_chainId}/blocks/head/helpers/preapply/operations",
                data: contents,
                cancellationToken: cancellationToken);
        }

        public Task<string> ForgeOperationsAsync(
            string branch,
            string operations,
            CancellationToken cancellationToken = default)
        {
            var contents = "{" +
                $"\"branch\":\"{branch}\"," +
                $"\"contents\":{operations}" +
            "}";

            return QueryAsync(
                query: $"chains/{_chainId}/blocks/head/helpers/forge/operations",
                data: contents,
                cancellationToken: cancellationToken);
        }

        public async Task<TezosRpcAccount> GetAccountAsync(
            string address,
            string blockHash = "head",
            CancellationToken cancellationToken = default)
        {
            var response = await QueryAsync(
                    query: $"chains/{_chainId}/blocks/{blockHash}/context/contracts/{address}",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return JsonSerializer.Deserialize<TezosRpcAccount>(response)!;
        }

        public Task<string> GetManagerKeyAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return QueryAsync(
                query: $"chains/{_chainId}/blocks/head/context/contracts/{address}/manager_key",
                cancellationToken: cancellationToken);
        }

        public async Task<TezosRpcHeader> GetHeaderAsync(
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            var response = await QueryAsync(
                    query: $"chains/{_chainId}/blocks/head{(offset != 0 ? $"~{offset}" : "")}/header",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return JsonSerializer.Deserialize<TezosRpcHeader>(response)!;
        }

        public async Task<TezosRpcDelegates> GetDelegateAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var response = await QueryAsync(
                    query: $"chains/{_chainId}/blocks/head/context/delegates/{address}",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return JsonSerializer.Deserialize<TezosRpcDelegates>(response)!;
        }

        private async Task<string> QueryAsync(
            string query,
            string? data = null,
            CancellationToken cancellationToken = default)
        {
            var isGet = data == null;

            var httpMethod = isGet
                ? HttpMethod.Get
                : HttpMethod.Post;

            var requestUri = new Uri(Url.Combine(_settings.Url, query));

            using var request = new HttpRequestMessage(httpMethod, requestUri);

            if (!string.IsNullOrEmpty(_settings.UserAgent))
                request.Headers.Add("User-Agent", _settings.UserAgent);

            request.Version = HttpVersion.Version11;

            if (!isGet)
            {
                request.Content = new StringContent(data);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            using var response = await HttpHelper.HttpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            var responseBody = await response.Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                // If failed, throw the body as the exception message.
                if (!string.IsNullOrWhiteSpace(responseBody))
                    throw new HttpRequestException(responseBody);

                // Otherwise, throw a generic exception.
                response.EnsureSuccessStatusCode();
            }

            return responseBody;
        }
    }
}