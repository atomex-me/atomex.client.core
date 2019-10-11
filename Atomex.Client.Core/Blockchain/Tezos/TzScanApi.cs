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
    public class TzScanApi : ITezosBlockchainApi
    {
        //private const string Mainnet = "https://api3.tzscan.io/";
        //private const string Alphanet = "https://api.alphanet.tzscan.io/";

        //private const string MainnetRpc = "https://mainnet-node.tzscan.io";
        //private const string AlphanetRpc = "http://alphanet-node.tzscan.io:80";
        //private const string AlphanetRpc = "https://tezos-dev.cryptonomic-infra.tech";
        //public const string ZeronetRpc = "https://zeronet-node.tzscan.io:80";

        internal class OperationHeader<T>
        {
            [JsonProperty("hash")]
            public string Hash { get; set; }
            [JsonProperty("block_hash")]
            public string BlockHash { get; set; }
            [JsonProperty("network_hash")]
            public string NetworkHash { get; set; }
            [JsonProperty("type")]
            public T Type { get; set; }
        }

        internal class Address
        {
            [JsonProperty("tz")]
            public string Tz { get; set; }
        }

        internal class Operation
        {
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("src")]
            public Address Source { get; set; }
            [JsonProperty("public_key")]
            public string PublicKey { get; set; }
            [JsonProperty("amount")]
            public string Amount { get; set; }
            [JsonProperty("destination")]
            public Address Destination { get; set; }
            [JsonProperty("str_parameters")]
            public string Parameters { get; set; }
            [JsonProperty("failed")]
            public bool Failed { get; set; }
            [JsonProperty("internal")]
            public bool Internal { get; set; }
            [JsonProperty("burn")]
            public long Burn { get; set; }
            [JsonProperty("counter")]
            public long Counter { get; set; }
            [JsonProperty("fee")]
            public long Fee { get; set; }
            [JsonProperty("gas_limit")]
            public string GasLimit { get; set; }
            [JsonProperty("storage_limit")]
            public string StorageLimit { get; set; }
            [JsonProperty("op_level")]
            public long OpLevel { get; set; }
            [JsonProperty("timestamp")]
            public DateTime TimeStamp { get; set; }
        }

        internal class OperationType<T>
        {
            [JsonProperty("kind")]
            public string Kind { get; set; }
            [JsonProperty("source")]
            public Address Source { get; set; }
            [JsonProperty("operations")]
            public List<T> Operations { get; set; }
        }

        private readonly Currency _currency;
        private readonly string _rpcNodeUri;
        private readonly string _apiBaseUrl;

        public TzScanApi(Atomex.Tezos currency)
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
                var tx = (TezosTransaction) transaction;
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
            var requestUri = $"v3/operation/{txId}";

            return await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var txs = TxsFromOperation(JsonConvert.DeserializeObject<OperationHeader<OperationType<Operation>>>(content))
                            .Cast<TezosTransaction>()
                            .ToList();

                        var internalTxs = txs
                            .Where(t => t.IsInternal)
                            .ToList();

                        var tx = txs.FirstOrDefault(t => !t.IsInternal);

                        if (tx == null)
                            return new Result<IBlockchainTransaction>((IBlockchainTransaction) null);

                        if (internalTxs.Any())
                            tx.InternalTxs = internalTxs;

                        return new Result<IBlockchainTransaction>(tx);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var txs = new List<IBlockchainTransaction>();

            for (var p = 0; ; p++)
            {
                var result = await GetTransactionsAsync(address, p, cancellationToken)
                    .ConfigureAwait(false);

                if (result.HasError)
                    return result;

                if (result.Value == null || !result.Value.Any())
                    break;

                txs.AddRange(result.Value);
            }

            return new Result<IEnumerable<IBlockchainTransaction>>(txs);
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTransactionsAsync(
            string address,
            int page,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"v3/operations/{address}?type=Transaction&p={page}"; // TODO: use all types!!!

            return await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var operations = JsonConvert
                            .DeserializeObject<List<OperationHeader<OperationType<Operation>>>>(content);

                        var txs = new List<IBlockchainTransaction>();

                        foreach (var operation in operations)    
                            txs.AddRange(TxsFromOperation(operation));
                        
                        return new Result<IEnumerable<IBlockchainTransaction>>(txs);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<bool>> IsActiveAddress(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"v3/number_operations/{address}?type=Transaction";

            return await HttpHelper.GetAsyncResult(
                    baseUri: _apiBaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var operationsCount = JsonConvert.DeserializeObject<JArray>(content)
                            .FirstOrDefault()?.Value<long>() ?? 0;

                        return new Result<bool>(operationsCount > 0);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private IEnumerable<IBlockchainTransaction> TxsFromOperation(
            OperationHeader<OperationType<Operation>> operationHeader)
        {
            var txs = new List<TezosTransaction>();

            var internalCounters = new Dictionary<string, int>();

            foreach (var operation in operationHeader.Type.Operations)
            {
                try
                {
                    if (operation.Kind != OperationType.Transaction)
                    {
                        Log.Debug("Skip {@kind} operation", operation.Kind);
                        continue;
                    }

                    var internalIndex = 0;

                    if (operation.Internal)
                    {
                        if (internalCounters.TryGetValue(operationHeader.Hash, out var index))
                        {
                            internalIndex = ++index;
                            internalCounters[operationHeader.Hash] = internalIndex;
                        }
                        else
                        {
                            internalCounters.Add(operationHeader.Hash, internalIndex);
                        }
                    }

                    var tx = new TezosTransaction
                    {
                        Id = operationHeader.Hash,
                        Currency = _currency,
                        State = operation.Failed
                            ? BlockchainTransactionState.Failed
                            : BlockchainTransactionState.Confirmed,
                        Type = BlockchainTransactionType.Unknown,        
                        CreationTime = operation.TimeStamp,

                        From = operation.Source.Tz,
                        To = operation.Destination.Tz,
                        Amount = decimal.Parse(operation.Amount),
                        Fee = operation.Fee,
                        GasLimit = decimal.Parse(operation.GasLimit),
                        StorageLimit = decimal.Parse(operation.StorageLimit),
                        Burn = operation.Burn,
                        Params = operation.Parameters != null
                            ? JObject.Parse(operation.Parameters)
                            : null,

                        IsInternal = operation.Internal,
                        InternalIndex = internalIndex,

                        BlockInfo = new BlockInfo
                        {
                            Confirmations = operation.Failed ? 0 : 1,
                            BlockHash = operationHeader.BlockHash,
                            BlockHeight = operation.OpLevel,
                            BlockTime = operation.TimeStamp,
                            FirstSeen = operation.TimeStamp,
                        }
                    };

                    txs.Add(tx);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Operation parse error");
                }
            }

            return txs;
        }
    }
}