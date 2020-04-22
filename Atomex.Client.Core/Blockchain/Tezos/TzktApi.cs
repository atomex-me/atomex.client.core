using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Tezos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Blockchain.Tezos
{
    public class TzktApi : BlockchainApi, ITezosBlockchainApi, ITokenBlockchainApi
    {
        private const string Tezos = "XTZ";

        private readonly Currency _currency;
        private readonly string _baseUri;
        private readonly string _rpcNodeUri;
        private readonly HttpRequestHeaders _headers;

        public TzktApi(Atomex.Tezos currency)
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
            try
            {
                var tx = (TezosTransaction)transaction;
                tx.State = BlockchainTransactionState.Pending;

                var rpc = new Rpc(_rpcNodeUri);

                var opResults = await rpc
                    .PreApplyOperations(tx.Head, tx.Operations, tx.SignedMessage.EncodedSignature)
                    .ConfigureAwait(false);

                if (!opResults.Any())
                    return new Error(Errors.EmptyPreApplyOperations, "Empty pre apply operations");

                string txId = null;

                foreach (var opResult in opResults)
                    Log.Debug("OperationResult {@result}: {@opResult}", opResult.Succeeded, opResult.Data.ToString());

                if (opResults.Any() && opResults.All(op => op.Succeeded))
                {
                    var injectedOperation = await rpc
                        .InjectOperations(tx.SignedMessage.SignedBytes)
                        .ConfigureAwait(false);

                    txId = injectedOperation.ToString();
                }

                if (txId == null)
                    return new Error(Errors.NullTxId, "Null tx id");

                tx.Id = txId;

                return tx.Id;
            }
            catch (Exception e)
            {
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
            var requestUri = $"operations/transactions/{txId}";

            return await HttpHelper.GetAsyncResult<IBlockchainTransaction>(
                    baseUri: _baseUri,
                    requestUri: requestUri,
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var txResult = ParseTxs(JsonConvert.DeserializeObject<JArray>(content));

                        if (txResult.HasError)
                            return txResult.Error;

                        return txResult.Value?.FirstOrDefault();
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            if (_currency.Name != Tezos)
                return await GetTokenTransactionsAsync(address, cancellationToken)
                    .ConfigureAwait(false);

            var requestUri = $"accounts/{address}/operations?type=transaction";

            var txsResult = await HttpHelper.GetAsyncResult(
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

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTokenTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var token = _currency as TezosTokens.FA12;

            var requestUri = $"tokens/{token.BcdNetwork}/{address}/transfers?size=10000"; // todo: use contract filter {token.TokenContractAddress}";

            var txsResult = await HttpHelper.GetAsyncResult(
                    baseUri: token.BcdApi,
                    requestUri: requestUri,
                    headers: _headers,
                    responseHandler: (response, content) => ParseTokenTxs(JsonConvert.DeserializeObject<JObject>(content)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (txsResult == null)
                return new Error(Errors.RequestError, $"Connection error while getting input transactions for address {address}");

            if (txsResult.HasError)
                return txsResult.Error;

            return new Result<IEnumerable<IBlockchainTransaction>>(txsResult.Value);
        }

        //public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTokenTransactionsAsync(
        //    string address,
        //    CancellationToken cancellationToken = default)
        //{
        //    var token = _currency as TezosTokens.FA12;

        //    var requestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transfer\"*{address}*";

        //    var txsResult = await HttpHelper.GetAsyncResult(
        //            baseUri: _baseUri,
        //            requestUri: requestUri,
        //            headers: _headers,
        //            responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)),
        //            cancellationToken: cancellationToken)
        //        .ConfigureAwait(false);

        //    if (txsResult == null)
        //        return new Error(Errors.RequestError, $"Connection error while getting input transactions for address {address}");

        //    if (txsResult.HasError)
        //        return txsResult.Error;

        //    return new Result<IEnumerable<IBlockchainTransaction>>(txsResult.Value);
        //}

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> TryGetTransactionsAsync(
            string address,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionsAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
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
                                Address = address,
                                IsAllocated = false,
                                IsRevealed = false,
                                LastCheckTimeUtc = DateTime.UtcNow
                            };
                        }

                        if (type == "user")
                        {
                            return new TezosAddressInfo
                            {
                                Address = address,
                                IsAllocated = decimal.Parse(addressInfo["balance"].Value<string>()) > 0,
                                IsRevealed = addressInfo["revealed"].Value<bool>(),
                                LastCheckTimeUtc = DateTime.UtcNow
                            };
                        }

                        return new TezosAddressInfo
                        {
                            Address = address,
                            IsAllocated = true,
                            IsRevealed = true,
                            LastCheckTimeUtc = DateTime.UtcNow
                        };
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
                return false;

            if (addressInfo.HasError)
                return addressInfo.Error;

            return addressInfo.Value.IsRevealed;
        }

        private Result<IEnumerable<TezosTransaction>> ParseTxs(JArray data)
        {
            var result = new List<TezosTransaction>();

            foreach (var op in data)
            {
                if (!(op is JObject transaction))
                    return new Error(Errors.NullOperation, "Null operation in response");

                var isInternal = transaction.ContainsKey("nonce");

                var isToken = _currency.Name != Tezos;
                var hasInternals = transaction["hasInternals"].Value<bool>();

                if (isToken && hasInternals)
                    continue;

                var state = StateFromStatus(transaction["status"].Value<string>());

                var tx = new TezosTransaction()
                {
                    Id = transaction["hash"].ToString(),
                    Currency = _currency,
                    State = state,
                    Type = BlockchainTransactionType.Unknown,
                    CreationTime = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc),

                    GasUsed = transaction["gasUsed"].Value<decimal>(),
                    Burn = transaction["storageFee"].Value<decimal>() +
                           transaction["allocationFee"].Value<decimal>(),

                    IsInternal = isInternal,
                    InternalIndex = 0,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = state == BlockchainTransactionState.Failed ? 0 : 1,
                        BlockHash = null,
                        BlockHeight = transaction["level"].Value<long>(),
                        BlockTime = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc),
                        FirstSeen = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc)
                    }
                };

                if (isToken)
                {
                    var parameters = transaction.ContainsKey("parameters")
                        ? JObject.Parse(transaction["parameters"].Value<string>())
                        : null;

                    if (parameters?["value"]?["args"]?[0]?["string"] != null)
                    {
                        tx.From = parameters?["value"]?["args"]?[0]?["string"]?.ToString();
                        tx.To = parameters?["value"]?["args"]?[1]?["args"]?[0]?["string"]?.ToString();
                    }
                    else
                    {
                        tx.From = Unforge.UnforgeAddress(parameters?["value"]?["args"]?[0]?["bytes"]?.ToString());
                        tx.To = Unforge.UnforgeAddress(parameters?["value"]?["args"]?[1]?["args"]?[0]?["bytes"]?.ToString());
                    }

                    tx.Amount = parameters?["value"]?["args"]?[1]?["args"]?[1]?["int"]?.Value<decimal>() ?? 0;
                    tx.Params = parameters;
                }
                else
                {
                    tx.From = transaction["sender"]?["address"]?.ToString();
                    tx.To = transaction["target"]?["address"]?.ToString();
                    tx.Amount = transaction["amount"].Value<decimal>();
                //}

                //if (!isToken)
                //{
                    if (isInternal)
                    {
                        tx.InternalIndex = transaction["nonce"]?.Value<int>() ?? 0;
                    }
                    else
                    {
                        var txParameters = transaction.ContainsKey("parameters")
                            ? JObject.Parse(transaction["parameters"].Value<string>())
                            : null;

                        tx.Params = txParameters;//?["value"] as JObject;
                        tx.Fee = transaction["bakerFee"].Value<decimal>();
                        tx.GasLimit = transaction["gasLimit"].Value<decimal>();
                        tx.StorageLimit = transaction["storageLimit"].Value<decimal>();
                    }
                }

                result.Add(tx);
            }

            return result;
        }

        private Result<IEnumerable<TezosTransaction>> ParseTokenTxs(JObject data)
        {
            var token = _currency as TezosTokens.FA12;

            var result = new List<TezosTransaction>();

            var transfers = data["transfers"] as JArray;

            foreach (var transfer in transfers)
            {
                var tx = transfer as JObject;
                var contract = tx["contract"].Value<string>();

                if (!contract.Equals(token.TokenContractAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                var state = StateFromStatus(tx["status"].Value<string>());
                var timeStamp = DateTime.SpecifyKind(DateTime.Parse(tx["timestamp"].ToString()), DateTimeKind.Utc);

                result.Add(new TezosTransaction
                {
                    Id = tx["hash"].Value<string>(),
                    Currency = _currency,
                    State = state,
                    Type = BlockchainTransactionType.Unknown,
                    CreationTime = timeStamp,
                    GasUsed = 0,
                    Burn = 0,
                    IsInternal = true,
                    InternalIndex = 0,
                    From = tx["from"].Value<string>(),
                    To = tx["to"].Value<string>(),
                    Amount = tx["amount"].Value<decimal>(),

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = state == BlockchainTransactionState.Failed ? 0 : 1,
                        BlockHash = null,
                        BlockHeight = tx["level"].Value<long>(),
                        BlockTime = timeStamp,
                        FirstSeen = timeStamp
                    }
                });
            }

            return result;
        }

        private BlockchainTransactionState StateFromStatus(string status) =>
            status switch
            {
                "applied" => BlockchainTransactionState.Confirmed,
                "backtracked" => BlockchainTransactionState.Failed,
                "skipped" => BlockchainTransactionState.Failed,
                "failed" => BlockchainTransactionState.Failed,
                _ => BlockchainTransactionState.Unknown
            };

        #region ITokenBlockchainApi

        public async Task<Result<decimal>> GetTokenBalanceAsync(
            string address,
            string callingAddress,
            SecureBytes securePublicKey,
            CancellationToken cancellationToken = default)
        {
            var token = _currency as TezosTokens.FA12;

            try
            {
                var rpc = new Rpc(_rpcNodeUri);

                var head = await rpc
                    .GetHeader()
                    .ConfigureAwait(false);

                var tx = new TezosTransaction
                {
                    Currency = token,
                    From = callingAddress,
                    To = token.TokenContractAddress,
                    Fee = token.GetBalanceFee,
                    GasLimit = token.GetBalanceGasLimit,
                    StorageLimit = token.GetBalanceStorageLimit,
                    Params = GetBalanceParams(address, token.ViewContractAddress),
                };

                await tx.FillOperationsAsync(
                        head: head,
                        securePublicKey:securePublicKey,
                        incrementCounter: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var runResults = await rpc
                    .RunOperations(head, tx.Operations)
                    .ConfigureAwait(false);

                Log.Debug("getTokenBalance result {@result}", runResults);

                return runResults?["contents"]?.LastOrDefault()?["metadata"]?["internal_operation_results"]?[0]?["result"]?["errors"]?[1]?["with"]?["args"]?[0]?["args"]?[0]?["int"]?.Value<decimal>();
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public async Task<Result<decimal>> TryGetTokenBalanceAsync(
            string address,
            string callingAddress,
            SecureBytes securePublicKey,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTokenBalanceAsync(address, callingAddress, securePublicKey, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting balance after {attempts} attempts");
        }

        #endregion

        private JObject GetBalanceParams(string holderAddress, string viewContractAddress)
        {
            return JObject.Parse(@"{'entrypoint':'getBalance','value':{'args': [{'string':'" + holderAddress + "'},{'string':'" + viewContractAddress + "%viewNat'}],'prim': 'Pair'}}");
        }
    }
}
