using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.Core.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Atomex.Blockchain.Tezos
{
    public class BabyTzktApi : ITezosBlockchainApi
    {
        //private const string Mainnet = "https://api3.tzscan.io/";
        //private const string Alphanet = "https://baby.tzkt.io/";
        //private const string Alphanet = "https://api.babylonnet.tzscan.io/";

        //private const string MainnetRpc = "https://mainnet-node.tzscan.io";
        //private const string AlphanetRpc = "http://alphanet-node.tzscan.io:80";
        //private const string AlphanetRpc = "https://tezos-dev.cryptonomic-infra.tech";
        //public const string ZeronetRpc = "https://zeronet-node.tzscan.io:80";

        private readonly Currency _currency;
        private readonly string _rpcNodeUri;
        private readonly string _apiBaseUrl;

        public BabyTzktApi(Atomex.Tezos currency)
        {
            _currency = currency;
            _rpcNodeUri = currency.RpcNodeUri;
            _apiBaseUrl = currency.BaseUri;
        }

        public async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var rpc = new Rpc(_rpcNodeUri);

                var balance = await rpc.GetBalance(address)
                    .ConfigureAwait(false);

                return new Result<decimal>(balance);
            }
            catch (Exception e)
            {
                return new Result<decimal>(new Error(Errors.RequestError, e.Message));
            }
        }

        public async Task<Result<string>> BroadcastAsync(
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
                    return new Result<string>(new Error(Errors.RequestError, "Empty pre apply operations"));

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
                    return new Result<string>(new Error(Errors.RequestError, "Null tx id"));

                tx.Id = txId;

                return new Result<string>(tx.Id);
            }
            catch (Exception e)
            {
                return new Result<string>(new Error(Errors.RequestError, e.Message));
            }
        }

        public async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"transactions/{txId}";

            return await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var txResult = ParseTxs(JsonConvert.DeserializeObject<JArray>(content));

                        if (txResult.HasError)
                            return new Result<IBlockchainTransaction>(txResult.Error);

                        var tx = txResult.Value?.FirstOrDefault();

                        if (tx == null)
                            return new Result<IBlockchainTransaction>((IBlockchainTransaction)null);

                        return new Result<IBlockchainTransaction>(tx);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var inputTxsResult = await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: $"transactions?receiver={address}",
                    responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (inputTxsResult.HasError)
                return new Result<IEnumerable<IBlockchainTransaction>>(inputTxsResult.Error);

            var outputTxsResult = await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: $"transactions?sender={address}",
                    responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (outputTxsResult.HasError)
                return new Result<IEnumerable<IBlockchainTransaction>>(outputTxsResult.Error);

            return new Result<IEnumerable<IBlockchainTransaction>>(inputTxsResult.Value.Concat(outputTxsResult.Value));
        }

        public async Task<Result<bool>> IsActiveAddress(
            string address,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: $"accounts/{address}",
                    responseHandler: (response, content) => new Result<bool>(content == "true"),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private Result<IEnumerable<TezosTransaction>> ParseTxs(JArray data)
        {
            var result = new List<TezosTransaction>();

            foreach (var op in data)
            {
                if (!(op is JObject operation))
                    return new Result<IEnumerable<TezosTransaction>>(new Error(Errors.RequestError, "Null operation in response"));

                var content = operation["content"] as JObject;

                var isInternal = operation["internal"].Value<bool>();

                var status = content["metadata"]?["operation_result"]?["status"]?.ToString() ?? content["result"]?["status"]?.ToString();

                var state = status != null && status == "applied"
                    ? BlockchainTransactionState.Confirmed
                    : BlockchainTransactionState.Failed;

                var tx = new TezosTransaction()
                {
                    Id = operation["hash"].ToString(),
                    Currency = _currency,
                    State = state,
                    Type = BlockchainTransactionType.Unknown,
                    CreationTime = DateTime.SpecifyKind(DateTime.Parse(operation["timestamp"].ToString()), DateTimeKind.Utc),

                    From = content["source"].ToString(),
                    To = content["destination"].ToString(),
                    Amount = content["amount"].Value<decimal>(),
                    Burn = 0, // todo: fix

                    IsInternal = isInternal,
                    InternalIndex = 0,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = state == BlockchainTransactionState.Failed ? 0 : 1,
                        BlockHash = null,
                        BlockHeight = operation["level"].Value<long>(),
                        BlockTime = DateTime.SpecifyKind(DateTime.Parse(operation["timestamp"].ToString()), DateTimeKind.Utc),
                        FirstSeen = DateTime.SpecifyKind(DateTime.Parse(operation["timestamp"].ToString()), DateTimeKind.Utc)
                    }
                };

                if (isInternal)
                {
                    tx.InternalIndex = content["nonce"].Value<int>();
                }
                else
                {
                    tx.Params = content["parameters"]?["value"] as JObject;
                    tx.Fee = content["fee"].Value<decimal>();
                    tx.GasLimit = content["gas_limit"].Value<decimal>();
                    tx.StorageLimit = content["storage_limit"].Value<decimal>();
                    //tx.InternalTxs = new List<TezosTransaction>();
                }

                result.Add(tx);
            }

            return new Result<IEnumerable<TezosTransaction>>(result);
        }
    }
}