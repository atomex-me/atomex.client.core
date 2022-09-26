using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Tezos.Internal.OperationResults;
using Atomex.Common;

namespace Atomex.Blockchain.Tezos.Internal
{
    public enum Chain
    {
        Main = 0,
        Test = 1
    }

    public class Rpc
    {
        private static readonly Dictionary<string, IOperationHandler> OpHandlers = new()
        {
            { OperationType.ActivateAccount, new ActivateAccountOperationHandler() },
            { OperationType.Transaction, new TransactionOperationHandler() },
            { OperationType.Reveal, new RevealOperationHandler() },
            { OperationType.Delegation, new DelegationOperationHandler() }
        };

        private readonly string _provider;
        private readonly string _chain;

        public Rpc(string provider)
            : this(provider, Chain.Main)
        { }

        public Rpc(string provider, Chain chain)
            : this(provider, chain.ToString().ToLower())
        { }

        public Rpc(string provider, string chain)
        {
            if (string.IsNullOrWhiteSpace(provider))

                throw new ArgumentException("Provider required", nameof(provider));

            if (string.IsNullOrWhiteSpace(chain))

                throw new ArgumentException("Chain required", nameof(chain));

            _provider = provider;
            _chain = chain;
        }

        public Task<JObject> GetDelegate(string address) =>
            QueryJ<JObject>($"chains/{_chain}/blocks/head/context/delegates/{address}");

        public Task<JObject> GetHeader() =>
            QueryJ<JObject>($"chains/{_chain}/blocks/head/header");

        public Task<JObject> GetHeader(int offset)
        {
            if (offset == 0)
                return GetHeader();

            return QueryJ<JObject>($"chains/{_chain}/blocks/head~{offset}/header");
        }

        public Task<JObject> GetAccount(string address) =>
            GetAccountForBlock("head", address);

        public Task<JObject> GetAccountForBlock(string blockHash, string address) =>
            QueryJ<JObject>($"chains/{_chain}/blocks/{blockHash}/context/contracts/{address}");

        public Task<JToken> GetManagerKey(string address) =>
            QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/manager_key");

        public async Task<bool> AutoFillOperations(
            TezosConfig tezosConfig,
            JObject head,
            JArray operations,
            bool useSafeStorageLimit = false,
            bool useDefaultFee = true)
        {
            JObject runResults = null;

            try
            {
                runResults = await RunOperations(head, operations)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "RunOperations error.");

                return false;
            }

            foreach (var result in runResults.SelectToken("contents"))
            {
                decimal gas = 0, storage_diff = 0, size = 0, fee = 0;

                var metaData = result["metadata"];
                var operationResult = metaData?["operation_result"];

                if (operationResult?["status"]?.ToString() == "applied")
                {
                    try
                    {
                        gas = tezosConfig.GasReserve + Math.Round(operationResult?["consumed_milligas"]?.Value<decimal>() / 1000 ?? 0);
                        gas += metaData
                            ?.SelectToken("internal_operation_results")
                            ?.Sum(res => Math.Round(res["result"]?["consumed_milligas"]?.Value<decimal>() / 1000 ?? 0)) ?? 0;

                        storage_diff = operationResult?["paid_storage_size_diff"]?.Value<decimal>() ?? 0;
                        storage_diff += tezosConfig.ActivationStorage * (operationResult?["allocated_destination_contract"]?.ToString() == "True" ? 1 : 0);
                        storage_diff += tezosConfig.ActivationStorage * metaData?["internal_operation_results"]
                            ?.Where(res => res["result"]?["allocated_destination_contract"]?.ToString() == "True")
                            .Count() ?? 0;
                        storage_diff += metaData
                            ?.SelectToken("internal_operation_results")
                            ?.Sum(res => res["result"]?["paid_storage_size_diff"]?.Value<decimal>() ?? 0) ?? 0;

                        var op = operations
                            .Children<JObject>()
                            .FirstOrDefault(o => o["counter"] != null && o["counter"].ToString() == result["counter"].ToString());

                        op["gas_limit"] = gas.ToString();

                        op["storage_limit"] = useSafeStorageLimit
                            ? Math.Max(op["storage_limit"].Value<decimal>(), storage_diff).ToString()
                            : storage_diff.ToString();

                        var forgedOpLocal = Forge.ForgeOperationsLocal(null, op);

                        size = forgedOpLocal.ToString().Length / 2 + Math.Ceiling((tezosConfig.HeadSizeInBytes + tezosConfig.SigSizeInBytes) / operations.Count);
                        fee = tezosConfig.MinimalFee + tezosConfig.MinimalNanotezPerByte * size + (long)Math.Ceiling(tezosConfig.MinimalNanotezPerGasUnit * gas) + 10;

                        if (useDefaultFee)
                            op["fee"] = fee.ToString();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Operation autofilling error: " + ex.Message.ToString());
                        return false;
                    }
                }
                else
                {
                    Log.Error("Operation running failed: ");
                    Log.Error(result.ToString());

                    //var isCounterInTheFuture = runResults.ToString().Contains("counter_in_the_future");

                    return false;
                }
            }

            return true;
        }

        public async Task<JObject> RunOperations(
            string branch,
            string chainId,
            string operations)
        {
            var contents = "{" +
                "\"operation\":{" +
                    $"\"branch\":\"{branch}\"," +
                    $"\"contents\":{operations}," +
                    $"\"signature\":\"edsigtePsnVcZ3FPzmenoU9NS1ubUsMmzSCmJgumPjUozCGLz7UwgpbPkpFP2LzC43pBS5B5tFNvDRbJ56s8by5W4Q4SrYPy6Qp\"" +
                "}," +
                $"\"chain_id\":\"{chainId}\"" +
            "}";

            var result = await Query(
                    ep: $"chains/{_chain}/blocks/head/helpers/scripts/run_operation",
                    data: contents)
                .ConfigureAwait(false);

            return JObject.Parse(result);
        }

        public Task<JObject> RunOperations(JObject blockHead, JArray operations) =>
            RunOperations(
                branch: blockHead["hash"].Value<string>(),
                chainId: blockHead["chain_id"].Value<string>(),
                operations: operations.ToString(Formatting.None));

        public Task<JToken> ForgeOperations(JObject blockHead, JToken operations)
        {
            if (operations is not JArray arrOps)
                arrOps = new JArray(operations);

            var contents = new JObject
            {
                ["branch"] = blockHead["hash"],
                ["contents"] = arrOps
            };

            return QueryJ($"chains/{_chain}/blocks/head/helpers/forge/operations", contents);
        }

        public async Task<List<OperationResult>> PreApplyOperations(
            JObject head,
            JArray operations,
            string signature)
        {
            var jsonObject = new JObject
            {
                ["protocol"] = head["protocol"],
                ["branch"] = head["hash"],
                ["contents"] = operations,
                ["signature"] = signature
            };

            var payload = new JArray
            {
                jsonObject
            };

            var result = await QueryJ<JArray>($"chains/{_chain}/blocks/head/helpers/preapply/operations", payload)
                .ConfigureAwait(false);

            return ParseApplyOperationsResult(result);
        }

        public async Task<JToken> InjectOperations(string signedBytes)
        {
            return await QueryJ<JValue>($"injection/operation?chain={_chain}", new JRaw($"\"{signedBytes}\""))
                .ConfigureAwait(false);
        }

        private List<OperationResult> ParseApplyOperationsResult(JArray appliedOps)
        {
            var operationResults = new List<OperationResult>();

            if (appliedOps?.Count > 0)
            {
                if (appliedOps.First["contents"] is not JArray contents)
                    return operationResults;

                foreach (var content in contents)
                {
                    var kind = content["kind"].ToString();

                    if (!string.IsNullOrWhiteSpace(kind))
                    {
                        var handler = OpHandlers[kind];

                        var opResult = handler?.ParseApplyOperationsResult(content);

                        if (opResult != null)
                            operationResults.Add(opResult);
                    }
                }
            }

            return operationResults;
        }

        private Task<JToken> QueryJ(string ep, JToken data = null) =>
            QueryJ<JToken>(ep, data);

        private async Task<TJType> QueryJ<TJType>(string ep, JToken data = null)
            where TJType : JToken
        {
            var result = await Query(ep, data?.ToString(Formatting.None))
                .ConfigureAwait(false);

            return (TJType)JToken.Parse(result);
        }

        private async Task<string> Query(
            string ep,
            object data = null)
        {
            var get = data == null;

            var httpMethod = get ? HttpMethod.Get : HttpMethod.Post;

            var requestUri = new Uri(Url.Combine(_provider, ep));

            using var request = new HttpRequestMessage(httpMethod, requestUri);
            request.Headers.Add("User-Agent", "Atomex");
            request.Version = HttpVersion.Version11;

            if (!get)
            {
                request.Content = new StringContent(data.ToString());
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                Log.Debug("Send request:\nUri: {requestUri}\nContent: {content}", requestUri, data.ToString());
            }

            using var response = await HttpHelper.HttpClient
                .SendAsync(request)
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