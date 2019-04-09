using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Atomix.Blockchain.Tezos.Internal.OperationResults;
using Atomix.Common;
using Atomix.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Atomix.Blockchain.Tezos.Internal
{
    public enum Chain
    {
        Main = 0,
        Test = 1
    }

    public class Rpc
    {
        //public const string DefaultProvider = "http://localhost:8732";

        private static readonly HttpClient Client = new HttpClient();
        private static readonly Dictionary<string, IOperationHandler> OpHandlers = new Dictionary<string, IOperationHandler>
        {
            { OperationType.ActivateAccount, new ActivateAccountOperationHandler() },
            { OperationType.Transaction, new TransactionOperationHandler() },
            { OperationType.Reveal, new RevealOperationHandler() }
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

        public Task<JObject> Describe()
        {
            // There is curently a weird situation in alpha where the RPC will not honor any request without a recurse=true arg. // 8 Aug 2018
            return QueryJ<JObject>("describe?recurse=true");
        }

        public Task<JObject> GetHead()
        {
            return QueryJ<JObject>($"chains/{_chain}/blocks/head");
        }

        public Task<JObject> GetHeader()
        {
            return QueryJ<JObject>($"chains/{_chain}/blocks/head/header");
        }
        public Task<JObject> GetBlockById(string id)
        {
            return QueryJ<JObject>($"chains/{_chain}/blocks/{id}");
        }

        public Task<JObject> GetAccountForBlock(string blockHash, string address)
        {
            return QueryJ<JObject>($"chains/{_chain}/blocks/{blockHash}/context/contracts/{address}");
        }

        public async Task<decimal> GetBalance(string address)
        {
            var response = await QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/balance")
                .ConfigureAwait(false);

            return decimal.Parse(response.ToString());
        }

        public Task<JObject> GetNetworkStat()
        {
            return QueryJ<JObject>("network/stat");
        }

        public async Task<int> GetCounter(string address)
        {
            var counter = await QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/counter")
                .ConfigureAwait(false);

            return Convert.ToInt32(counter.ToString());
        }

        public Task<JToken> GetManagerKey(string address)
        {
            return QueryJ($"chains/{_chain}/blocks/head/context/contracts/{address}/manager_key");
        }

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

        public async Task<SendTransactionOperationResult> SendTransaction(
            Keys keys,
            string from,
            string to,
            decimal amount,
            decimal fee,
            decimal gasLimit,
            decimal storageLimit,
            JObject param = null)
        {
            gasLimit = gasLimit != 0 ? gasLimit : 200;

            var head = await GetHeader()
                .ConfigureAwait(false);

            var account = await GetAccountForBlock(head["hash"].ToString(), from)
                .ConfigureAwait(false);

            var counter = int.Parse(account["counter"].ToString());

            var operations = new JArray();

            var managerKey = await GetManagerKey(from)
                .ConfigureAwait(false);

            var gas = gasLimit.ToString(CultureInfo.InvariantCulture);
            var storage = storageLimit.ToString(CultureInfo.InvariantCulture);

            if (keys != null && managerKey["key"] == null)
            {
                var revealOp = new JObject
                {
                    ["kind"] = OperationType.Reveal,
                    ["fee"] = "0",
                    ["public_key"] = keys.DecryptPublicKey(),
                    ["source"] = from,
                    ["storage_limit"] = storage,
                    ["gas_limit"] = gas,
                    ["counter"] = (++counter).ToString()
                };

                operations.AddFirst(revealOp);
            }

            var transaction = new JObject
            {
                ["kind"] = OperationType.Transaction,
                ["source"] = from,
                ["fee"] = fee.ToString(CultureInfo.InvariantCulture),
                ["counter"] = (++counter).ToString(),
                ["gas_limit"] = gas,
                ["storage_limit"] = storage,
                ["amount"] = Math.Round(amount.ToMicroTez(), 0).ToString(CultureInfo.InvariantCulture),
                ["destination"] = to
            };

            operations.Add(transaction);

            if (param != null)
                transaction["parameters"] = param;
            else
            {
                var parameters = new JObject
                {
                    ["prim"] = "Unit",
                    ["args"] = new JArray() // No args for this contract.
                };

                transaction["parameters"] = parameters;
            }

            var sendResults = await SendOperations(operations, keys, head)
                .ConfigureAwait(false);

            return sendResults.LastOrDefault() as SendTransactionOperationResult;
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
                signedOpGroup = new TezosSigner().SignHash(
                    data: Hex.FromString(forgedOpGroup.ToString()),
                    privateKey: Base58Check.Decode(keys.DecryptPrivateKey(), Prefix.Edsk),
                    watermark: Watermark.Generic);
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

        public Task<JToken> ForgeOperations(JObject blockHead, JArray operations)
        {
            var contents = new JObject
            {
                ["branch"] = blockHead["hash"],
                ["contents"] = operations
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

        private Task<JToken> QueryJ(string ep, JToken data = null)
        {
            return QueryJ<JToken>(ep, data);
        }

        private async Task<TJType> QueryJ<TJType>(string ep, JToken data = null)
            where TJType : JToken
        {
            var result = await Query(ep, data?.ToString(Formatting.None))
                .ConfigureAwait(false);

            return (TJType)JToken.Parse(result);
        }

        private async Task<string> Query(string ep, object data = null)
        {
            var get = data == null;

            var request = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, $"{_provider}/{ep}")
            {
                Version = HttpVersion.Version11 // Tezos node does not like the default v2.
            };

            if (!get)
            {
                request.Content = new StringContent(data.ToString());
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            var response = await Client
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