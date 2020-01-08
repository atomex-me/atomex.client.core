using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Atomex.Blockchain.Tezos.Internal.OperationResults;
using Atomex.Common;
using Atomex.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Blockchain.Tezos.Internal
{
    public enum Chain
    {
        Main = 0,
        Test = 1
    }

    public class Rpc
    {
        //public const string DefaultProvider = "http://localhost:8732";

        private static readonly Dictionary<string, IOperationHandler> OpHandlers = new Dictionary<string, IOperationHandler>
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

        public Task<JObject> Describe() =>
            QueryJ<JObject>("describe?recurse=true");

        public Task<JObject> GetHead() =>
            QueryJ<JObject>($"chains/{_chain}/blocks/head");

        public Task<JObject> GetDelegate(string address) =>
            QueryJ<JObject>($"chains/{_chain}/blocks/head/context/delegates/{address}");

        public Task<JObject> GetHeader() =>
            QueryJ<JObject>($"chains/{_chain}/blocks/head/header");

        public Task<JObject> GetBlockById(string id) =>
            QueryJ<JObject>($"chains/{_chain}/blocks/{id}");

        public Task<JObject> GetAccount(string address) =>
            GetAccountForBlock("head", address);

        public Task<JObject> GetAccountForBlock(string blockHash, string address) =>
            QueryJ<JObject>($"chains/{_chain}/blocks/{blockHash}/context/contracts/{address}");

        public async Task<decimal> GetBalance(string address)
        {
            var response = await QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/balance")
                .ConfigureAwait(false);

            return decimal.Parse(response.ToString());
        }

        public Task<JObject> GetNetworkStat() =>
            QueryJ<JObject>("network/stat");

        public async Task<int> GetCounter(string address)
        {
            var counter = await QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/counter")
                .ConfigureAwait(false);

            return Convert.ToInt32(counter.ToString());
        }

        public Task<JToken> GetManagerKey(string address) =>
            QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/manager_key");

        public async Task<ActivateAccountOperationResult> Activate(string address, string secret)
        {
            var activateOp = new JObject
            {
                ["kind"] = OperationType.ActivateAccount,
                ["pkh"] = address,
                ["secret"] = secret
            };

            var sendResults = await SendOperations(activateOp, null)
                .ConfigureAwait(false);

            return sendResults.LastOrDefault() as ActivateAccountOperationResult;
        }

        private async Task<List<OperationResult>> SendOperations(
            JToken operations,
            Keys keys,
            JObject head = null)
        {
            if (head == null)
                head = await GetHeader()
                    .ConfigureAwait(false);

            if (!(operations is JArray arrOps))
                arrOps = new JArray(operations);

            var forgedOpGroup = await ForgeOperations(head, arrOps)
                .ConfigureAwait(false);

            SignedMessage signedOpGroup;

            if (keys == null)
            {
                signedOpGroup = new SignedMessage
                {
                    SignedBytes = forgedOpGroup + "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000",
                    EncodedSignature = "edsigtXomBKi5CTRf5cjATJWSyaRvhfYNHqSUGrn4SdbYRcGwQrUGjzEfQDTuqHhuA8b2d8NarZjz8TRf65WkpQmo423BtomS8Q"
                };
            }
            else
            {
                var privateKey = Base58Check.Decode(keys.DecryptPrivateKey(), Prefix.Edsk);

                signedOpGroup = TezosSigner.SignHash(
                    data: Hex.FromString(forgedOpGroup.ToString()),
                    privateKey: privateKey,
                    watermark: Watermark.Generic,
                    isExtendedKey: privateKey.Length == 64);

                privateKey.Clear();
            }

            var opResults = await PreApplyOperations(head, arrOps, signedOpGroup.EncodedSignature)
                .ConfigureAwait(false);

            /////deleting too big contractCode from response
            //foreach (var opResult in opResults)
            //{
            //    if (opResult.Data?["metadata"]?["operation_result"]?["status"]?.ToString() == "failed")
            //    {
            //        foreach (JObject error in opResult.Data["metadata"]["operation_result"]["errors"])
            //        {
            //            if (error["contractCode"]?.ToString().Length > 1000)
            //                error["contractCode"] = "";
            //        }
            //    }
            //}

            if (opResults.Any() && opResults.All(op => op.Succeeded))
            {
                var injectedOperation = await InjectOperations(signedOpGroup.SignedBytes)
                    .ConfigureAwait(false);

                opResults.Last().Data["op_hash"] = injectedOperation.ToString();
            }

            return opResults;
        }

        public async Task<bool> AutoFillOperations(Atomex.Tezos tezos, JObject head, JArray operations, bool defaultFee = true)
        {
            var runResults = await RunOperations(head, operations)
                .ConfigureAwait(false);

            foreach (var result in runResults.SelectToken("contents"))
            {
                decimal gas = 0, storage = 0, storage_diff = 0, size = 0, fee = 0;

                var metaData = result["metadata"];
                var operationResult = metaData?["operation_result"];

                if (operationResult?["status"]?.ToString() == "applied")
                {
                    try
                    {
                        gas = tezos.GasReserve + operationResult?["consumed_gas"]?.Value<decimal>() ?? 0;
                        gas += metaData
                            ?.SelectToken("internal_operation_results")
                            ?.Sum(res => res["result"]?["consumed_gas"]?.Value<decimal>() ?? 0) ?? 0;

                        storage = operationResult?["storage_size"]?.Value<decimal>() ?? 0;

                        storage_diff = operationResult?["paid_storage_size_diff"]?.Value<decimal>() ?? 0;
                        storage_diff += tezos.ActivationStorage * (operationResult?["allocated_destination_contract"]?.ToString() == "True" ? 1 : 0);
                        storage_diff += tezos.ActivationStorage * metaData?["internal_operation_results"]
                            ?.Where(res => res["result"]?["allocated_destination_contract"]?.ToString() == "True")
                            .Count() ?? 0;

                        var op = operations
                            .Children<JObject>()
                            .FirstOrDefault(o => o["counter"] != null && o["counter"].ToString() == result["counter"].ToString());

                        op["gas_limit"] = gas.ToString();
                        op["storage_limit"] = storage_diff.ToString();

                        JToken forgedOpLocal = Forge.ForgeOperationsLocal(null, op);

                        //JToken forgedOp = await ForgeOperations(head, op);

                        //if (forgedOpLocal.ToString() != forgedOp.ToString().Substring((int)tezos.HeadSizeInBytes * 2))
                        //    Log.Error("Local operation forge result differs from remote"); //process the error

                        size = forgedOpLocal.ToString().Length / 2 + Math.Ceiling((tezos.HeadSizeInBytes + tezos.SigSizeInBytes) / operations.Count);
                        fee = tezos.MinimalFee + tezos.MinimalNanotezPerByte * size + (long)Math.Ceiling(tezos.MinimalNanotezPerGasUnit * gas) + 1;
                        if(defaultFee)
                            op["fee"] = fee.ToString();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Transaction autofilling error: " + ex.Message.ToString()); //process the error
                        return false;
                    }
                }
                else
                {
                    Log.Error("Transaction running failed: "); //process the error
                    Log.Error(result.ToString());
                    return false;
                }
            }
            return true;
        }

        private async Task<JObject> RunOperations(JObject blockHead, JArray operations)
        {
            var contents = new JObject
            {
                ["operation"] = new JObject()
                {
                    { "branch", blockHead["hash"] },
                    { "contents", operations },
                    { "signature", "edsigtePsnVcZ3FPzmenoU9NS1ubUsMmzSCmJgumPjUozCGLz7UwgpbPkpFP2LzC43pBS5B5tFNvDRbJ56s8by5W4Q4SrYPy6Qp" } //random sig
                },
                ["chain_id"] = blockHead["chain_id"]
            };

            var result = await QueryJ<JObject>($"chains/{_chain}/blocks/head/helpers/scripts/run_operation", contents)
                .ConfigureAwait(false);

            return result;
        }

        public Task<JToken> ForgeOperations(JObject blockHead, JToken operations)
        {
            if (!(operations is JArray arrOps))
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
                if (!(appliedOps.First["contents"] is JArray contents))
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

            var requestUri = $"{_provider}/{ep}";

            using var request = new HttpRequestMessage(httpMethod, requestUri);
            request.Headers.Add("User-Agent", "Atomex");
            request.Version = HttpVersion.Version11;

            if (!get)
            {
                request.Content = new StringContent(data.ToString());
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                Log.Debug("Send request:\nUri: {requestUri}\nContent: {content}", requestUri, data.ToString());
            }

            using var httpClient = new HttpClient();
            using var response = await httpClient
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