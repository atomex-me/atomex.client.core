using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Tezos.Internal;
using Atomex.Common;
using Atomex.Core;
using Atomex.Wallet.Tezos;

namespace Atomex.Blockchain.Tezos
{
    public class TzktApi : BlockchainApi, ITezosBlockchainApi, ITokenBlockchainApi
    {
        private const string Tezos = "XTZ";
        private const string tzBTC = "TZBTX";
        private const string NYX = "NYX";
        private const string FA2 = "FA2";


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
            var transaction = new TezosTransaction();
            var isParentTxValid = false;

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

                        if (!txResult.Value.Any())
                            return null;

                        var internalTxs = new List<TezosTransaction>();

                        foreach (var tx in txResult.Value)
                        {
                            if (!tx.IsInternal)
                            {
                                transaction = tx;
                                isParentTxValid = true;
                            }
                            else
                                internalTxs.Add(tx);
                        }

                        transaction.InternalTxs = internalTxs;

                        // replace non valid parent tx by internal tx
                        if (isParentTxValid == false && internalTxs.Count == 1)
                            transaction = internalTxs.First();

                        return transaction;
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

        private class TxsSource
        {
            public string BaseUri { get; set; }
            public string RequestUri { get; set; }
            public Func<string, Result<IEnumerable<TezosTransaction>>> Parser { get; set; }
        }

        public async Task<Result<IEnumerable<IBlockchainTransaction>>> GetTokenTransactionsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var txsSources = new List<TxsSource>();

            if (_currency.Name == NYX)
            {
                var token = _currency as TezosTokens.NYX;

                txsSources = new List<TxsSource>
                {
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?sender={address}&target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"setAllowTransferFrom\"*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transfer\"*{address}*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transfer\"*{Forge.ForgeAddress(address)}*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transferFrom\"*{address}*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transferFrom\"*{Forge.ForgeAddress(address)}*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    }
                };
            }
            else if (_currency.Name == FA2)
            {
                var token = _currency as TezosTokens.FA2;

                txsSources = new List<TxsSource>
                {
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?sender={address}&target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"update_operators\"*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transfer\"*{address}*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"transfer\"*{Forge.ForgeAddress(address)}*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    }
                };
            }
            else
            {
                var token = _currency as TezosTokens.FA12;

                txsSources = new List<TxsSource>
                {
                    new TxsSource
                    {
                        BaseUri = _baseUri,
                        RequestUri = $"operations/transactions?sender={address}&target={token.TokenContractAddress}&parameters.as=*\"entrypoint\":\"approve\"*",
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTxs(JsonConvert.DeserializeObject<JArray>(content)))
                    },
                    new TxsSource
                    {
                        BaseUri = token.BcdApi,
                        RequestUri = $"tokens/{token.BcdNetwork}/transfers/{address}?size=10000", // todo: use contract filter {token.TokenContractAddress}";
                        Parser = new Func<string, Result<IEnumerable<TezosTransaction>>>(content => ParseTokenTxs(JsonConvert.DeserializeObject<JObject>(content)))
                    },
                };
            }

            var txsResult = Enumerable.Empty<IBlockchainTransaction>();

            foreach (var txsSource in txsSources)
            {
                var txsRes = await HttpHelper.GetAsyncResult(
                        baseUri: txsSource.BaseUri,
                        requestUri: txsSource.RequestUri,
                        headers: _headers,
                        responseHandler: (response, content) => txsSource.Parser(content),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (txsRes == null)
                    return new Error(Errors.RequestError, $"Connection error while getting token approve transactions for address {address}");

                if (txsRes.HasError)
                    return txsRes.Error;

                txsResult = txsResult.Concat(txsRes.Value);
            }

            return new Result<IEnumerable<IBlockchainTransaction>>(txsResult);
        }

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

        public async Task<Result<Account>> GetAccountByAddressAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<Account>(
                    baseUri: _baseUri,
                    requestUri: $"accounts/{address}",
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var accountInfo = JsonConvert.DeserializeObject<JObject>(content);

                        var type = accountInfo["type"].Value<string>();

                        if (type == "user")
                        {
                            return new Account
                            {
                                Address = address,
                                DelegateAddress = accountInfo["delegate"]["address"].ToString(),
                                DelegationTime = DateTimeOffset.Parse(accountInfo["delegationTime"].ToString()).DateTime,
                                DelegationLevel = accountInfo["delegationLevel"].Value<decimal>()
                            };
                        }

                        return null;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<Result<decimal>> GetHeadLevelAsync(
             CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: _baseUri,
                    requestUri: $"head",
                    headers: _headers,
                    responseHandler: (response, content) =>
                    {
                        var head = JsonConvert.DeserializeObject<JObject>(content);

                        return head["level"].Value<decimal>();
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

            if (addressInfo == null)
                return null;

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
                return null;

            if (addressInfo.HasError)
                return addressInfo.Error;

            return addressInfo.Value.IsRevealed;
        }

        private TezosTransaction ParseFA12Params(TezosTransaction tx, JObject transaction)
        {
            var tokenContractAddress = (_currency as TezosTokens.FA12).TokenContractAddress;

            if (transaction["target"]?["address"]?.ToString() != tokenContractAddress)
            {
                Log.Debug(
                    "Error while parsing token transactions {@Id}",
                    transaction["hash"].ToString());
                return null;
            }

            //if (transaction["hasInternals"].Value<bool>())
            //    return null;

            tx.Fee = transaction["bakerFee"].Value<decimal>();
            tx.GasLimit = transaction["gasLimit"].Value<decimal>();
            tx.StorageLimit = transaction["storageLimit"].Value<decimal>();

            var parameters = transaction.ContainsKey("parameters")
                ? JObject.Parse(transaction["parameters"].Value<string>())
                : null;

            if (parameters?["entrypoint"]?.ToString() == "approve")
            {
                tx.Type = BlockchainTransactionType.TokenApprove;
                tx.From = transaction["sender"]?["address"]?.ToString();
                tx.To = transaction["target"]?["address"]?.ToString();
                tx.Amount = 0;
            }
            else if (parameters?["entrypoint"]?.ToString() == "transfer")
            {
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
            }
            else
            {
                Log.Error(
                    "Error while parsing FA12 token transactions {@Id}",
                    transaction["hash"].ToString());
                return null;
            }

            tx.Params = parameters;

            return tx;
        }

        private TezosTransaction ParseNYXParams(TezosTransaction tx, JObject transaction)
        {
            var tokenContractAddress = (_currency as TezosTokens.NYX).TokenContractAddress;

            tx.Fee = transaction["bakerFee"].Value<decimal>();
            tx.GasLimit = transaction["gasLimit"].Value<decimal>();
            tx.StorageLimit = transaction["storageLimit"].Value<decimal>();

            if (transaction["target"]?["address"]?.ToString() != tokenContractAddress)
            {
                Log.Warning(
                    "Unsupported data while parsing token transactions {@Id}",
                    transaction["hash"].ToString());
                return tx;
            }

            var parameters = transaction.ContainsKey("parameters")
                ? JObject.Parse(transaction["parameters"].Value<string>())
                : null;

            if (parameters?["entrypoint"]?.ToString() == "setAllowTransferFrom")
            {
                tx.Type = BlockchainTransactionType.TokenApprove;
                tx.From = transaction["sender"]?["address"]?.ToString();
                tx.To = transaction["target"]?["address"]?.ToString();
                tx.Amount = 0;
            }
            else if (parameters?["entrypoint"]?.ToString() == "transfer")
            {
                tx.From = transaction["sender"]?["address"]?.ToString();

                if (parameters?["value"]?[0]?["args"]?[1]?["string"] != null)
                    tx.To = parameters?["value"]?[0]?["args"]?[1]?["string"]?.ToString();
                else
                    tx.To = Unforge.UnforgeAddress(parameters?["value"]?[0]?["args"]?[1]?["bytes"]?.ToString());

                tx.Amount = parameters?["value"]?[0]?["args"]?[0]?["int"]?.Value<decimal>() ?? 0;
            }
            else if (parameters?["entrypoint"]?.ToString() == "transferFrom")
            {
                if (parameters?["value"]?[0]?["args"]?[1]?["string"] != null)
                {
                    tx.From = parameters?["value"]?[0]?["args"]?[0]?["args"]?[1]?["string"]?.ToString();
                    tx.To = parameters?["value"]?[0]?["args"]?[1]?["string"]?.ToString();
                }
                else
                {
                    tx.From = Unforge.UnforgeAddress(parameters?["value"]?[0]?["args"]?[0]?["args"]?[1]?["bytes"]?.ToString());
                    tx.To = Unforge.UnforgeAddress(parameters?["value"]?[0]?["args"]?[1]?["bytes"]?.ToString());
                }
                tx.Amount = parameters?["value"]?[0]?["args"]?[0]?["args"]?[0]?["int"]?.Value<decimal>() ?? 0;
            }
            else
            {
                Log.Error(
                    "Error while parsing NYX token transactions {@Id}",
                    transaction["hash"].ToString());
                return null;
            }

            tx.Params = parameters;

            return tx;
        }

        private TezosTransaction ParseFA2Params(TezosTransaction tx, JObject transaction)
        {
            var tokenContractAddress = (_currency as TezosTokens.FA2).TokenContractAddress;

            tx.Fee = transaction["bakerFee"].Value<decimal>();
            tx.GasLimit = transaction["gasLimit"].Value<decimal>();
            tx.StorageLimit = transaction["storageLimit"].Value<decimal>();

            if (transaction["target"]?["address"]?.ToString() != tokenContractAddress)
            {
                Log.Warning(
                    "Unsupported data while parsing token transactions {@Id}",
                    transaction["hash"].ToString());
                return tx;
            }

            var parameters = transaction.ContainsKey("parameters")
                ? JObject.Parse(transaction["parameters"].Value<string>())
                : null;

            if (parameters?["entrypoint"]?.ToString() == "update_operators")
            {
                tx.Type = BlockchainTransactionType.TokenApprove;
                tx.From = transaction["sender"]?["address"]?.ToString();
                tx.To = transaction["target"]?["address"]?.ToString();
                tx.Amount = 0;
            }
            else if (parameters?["entrypoint"]?.ToString() == "transfer")
            {
                if (parameters?["value"]?[0]?["args"]?[0]?["string"] != null)
                {
                    tx.From = parameters?["value"]?[0]?["args"]?[0]?["string"]?.ToString();
                    tx.To = parameters?["value"]?[0]?["args"]?[1]?[0]?["args"]?[0]?["string"]?.ToString();
                }
                else
                {
                    tx.From = Unforge.UnforgeAddress(parameters?["value"]?[0]?["args"]?[0]?["bytes"]?.ToString());
                    tx.To = Unforge.UnforgeAddress(parameters?["value"]?[0]?["args"]?[1]?[0]?["args"]?[0]?["bytes"]?.ToString());
                }
                tx.Amount = parameters?["value"]?[0]?["args"]?[1]?[0]?["args"]?[1]?["args"]?[1]?["int"]?.Value<decimal>() ?? 0;
            }
            else
            {
                Log.Error(
                    "Error while parsing FA2 token transactions {@Id}",
                    transaction["hash"].ToString());
                return null;
            }

            tx.Params = parameters;

            return tx;
        }

        private Result<IEnumerable<TezosTransaction>> ParseTxs(JArray data, bool parseTokenParams = true)
        {
            var result = new List<TezosTransaction>();

            foreach (var op in data)
            {
                if (!(op is JObject transaction))
                    return new Error(Errors.NullOperation, "Null operation in response");

                var state = StateFromStatus(transaction["status"].Value<string>());

                var alias = $"{transaction["sender"]["alias"]?.Value<string>()}/{transaction["target"]["alias"]?.Value<string>()}";

                if (alias.Length == 1) {
                    alias = String.Empty;
                }

                var tx = new TezosTransaction()
                {
                    Id       = transaction["hash"].ToString(),
                    Currency = _currency,
                    State    = state,
                    Type     = BlockchainTransactionType.Unknown,
                    CreationTime = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc),

                    GasUsed = transaction["gasUsed"].Value<decimal>(),
                    Burn = transaction["storageFee"].Value<decimal>() +
                           transaction["allocationFee"].Value<decimal>(),

                    Alias = alias,

                    IsInternal = transaction.ContainsKey("nonce"),
                    //tx.IsInternal = tx.From == ((TezosTokens.FA12) _currency).SwapContractAddress;
                    InternalIndex = transaction["nonce"]?.Value<int>() ?? 0,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = state == BlockchainTransactionState.Failed ? 0 : 1,
                        BlockHash     = null,
                        BlockHeight   = transaction["level"].Value<long>(),
                        BlockTime     = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc),
                        FirstSeen     = DateTime.SpecifyKind(DateTime.Parse(transaction["timestamp"].ToString()), DateTimeKind.Utc)
                    }
                };

                if (_currency.Name != Tezos && parseTokenParams)
                {
                    if (_currency.Name == NYX)
                        tx = ParseNYXParams(tx, transaction);
                    else if (_currency.Name == FA2)
                        tx = ParseFA2Params(tx, transaction);
                    else
                        tx = ParseFA12Params(tx, transaction);
                }
                else
                {
                    tx.From   = transaction["sender"]?["address"]?.ToString();
                    tx.To     = transaction["target"]?["address"]?.ToString();
                    tx.Amount = transaction["amount"].Value<decimal>();
                    tx.Alias  = alias;

                    if (tx.IsInternal)
                    {
                        tx.InternalIndex = transaction["nonce"]?.Value<int>() ?? 0;
                    }
                    else
                    {
                        var txParameters = transaction.ContainsKey("parameters")
                            ? JObject.Parse(transaction["parameters"].Value<string>())
                            : null;

                        tx.Params       = txParameters;
                        tx.Fee          = transaction["bakerFee"].Value<decimal>();
                        tx.GasLimit     = transaction["gasLimit"].Value<decimal>();
                        tx.StorageLimit = transaction["storageLimit"].Value<decimal>();
                    }
                }

                if (tx != null)
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

                var alias = $"{tx["from_alias"]?.Value<string>()}/{tx["to_alias"]?.Value<string>()}";

                if (alias.Length == 1) {
                    alias = String.Empty;
                }

                if (!contract.Equals(token.TokenContractAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                var state = StateFromStatus(tx["status"].Value<string>());
                var timeStamp = DateTime.SpecifyKind(DateTime.Parse(tx["timestamp"].ToString()), DateTimeKind.Utc);

                result.Add(new TezosTransaction
                {
                    Id            = tx["hash"].Value<string>(),
                    Currency      = _currency,
                    State         = state,
                    Type          = BlockchainTransactionType.Unknown,
                    CreationTime  = timeStamp,
                    GasUsed       = 0,
                    Burn          = 0,
                    IsInternal    = true,
                    InternalIndex = 0,

                    From          = tx["from"].Value<string>(),
                    To            = tx["to"].Value<string>(),
                    Amount        = tx["amount"].Value<decimal>(),
                    Alias         = alias,

                    BlockInfo = new BlockInfo
                    {
                        Confirmations = state == BlockchainTransactionState.Failed ? 0 : 1,
                        BlockHash     = null,
                        BlockHeight   = tx["level"].Value<long>(),
                        BlockTime     = timeStamp,
                        FirstSeen     = timeStamp
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
                    Fee = 0, //token.GetBalanceFee,
                    GasLimit = token.GetBalanceGasLimit,
                    StorageLimit = 0, //token.GetBalanceStorageLimit,
                    Params = GetBalanceParams(address, token.ViewContractAddress),
                };

                await tx.FillOperationsAsync(
                        head: head,
                        securePublicKey: securePublicKey,
                        incrementCounter: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var runResults = await rpc
                    .RunOperations(head, tx.Operations)
                    .ConfigureAwait(false);

                //Log.Debug("getTokenBalance result {@result}", runResults.ToString());

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

        public async Task<Result<decimal>> GetTokenBigMapBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            if (_currency.Name == NYX)
            {
                var token = _currency as TezosTokens.NYX;

                try
                {
                    return await HttpHelper.GetAsyncResult<decimal>(
                        baseUri: token.BcdApi,
                        requestUri: $"bigmap/{token.BcdNetwork}/{token.TokenPointerBalance}/keys?q={address}",
                        headers: _headers,
                        responseHandler: (response, content) =>
                        {
                            var bigMap = JsonConvert.DeserializeObject<JArray>(content);

                            if (bigMap.Count == 0)
                                return 0;

                            return bigMap[0]["data"]?["value"]?["value"]?.Value<decimal>() ?? 0;
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    return new Error(Errors.RequestError, e.Message);
                }
            }
            else if (_currency.Name == FA2)
            {
                var token = _currency as TezosTokens.FA2;

                try
                {
                    return await HttpHelper.GetAsyncResult<decimal>(
                        baseUri: token.BcdApi,
                        requestUri: $"bigmap/{token.BcdNetwork}/{token.TokenPointerBalance}/keys?q={token.TokenID}",
                        headers: _headers,
                        responseHandler: (response, content) =>
                        {
                            var bigMap = JsonConvert.DeserializeObject<JArray>(content);

                            if (bigMap.Count == 0)
                                return 0;

                            return bigMap[0]["data"]?["value"]?["value"]?.Value<string>() == address ? 1 : 0;
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    return new Error(Errors.RequestError, e.Message);
                }
            }
            else
                return null;
        }

        public async Task<Result<decimal>> TryGetTokenBigMapBalanceAsync(
            string address,
            int pointer,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTokenBigMapBalanceAsync(address, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting big map balance after {attempts} attempts");
        }

        public async Task<Result<decimal>> GetTokenAllowanceAsync(
            string holderAddress,
            string spenderAddress,
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
                    Fee = 0, //token.GetAllowanceFee,
                    GasLimit = token.GetAllowanceGasLimit,
                    StorageLimit = 0, //token.GetAllowanceStorageLimit,
                    Params = GetAllowanceParams(holderAddress, spenderAddress, token.ViewContractAddress),
                };

                await tx.FillOperationsAsync(
                        head: head,
                        securePublicKey: securePublicKey,
                        incrementCounter: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var runResults = await rpc
                    .RunOperations(head, tx.Operations)
                    .ConfigureAwait(false);

                //Log.Debug("getTokenAllowance result {@result}", runResults);

                return runResults?["contents"]?.LastOrDefault()?["metadata"]?["internal_operation_results"]?[0]?["result"]?["errors"]?[1]?["with"]?["args"]?[0]?["args"]?[0]?["int"]?.Value<decimal>();
            }
            catch (Exception e)
            {
                return new Error(Errors.RequestError, e.Message);
            }
        }

        public async Task<Result<decimal>> TryGetTokenAllowanceAsync(
            string holderAddress,
            string spenderAddress,
            string callingAddress,
            SecureBytes securePublicKey,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTokenAllowanceAsync(holderAddress, spenderAddress, callingAddress, securePublicKey, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting balance after {attempts} attempts");
        }

        public async Task<Result<decimal>> GetTokenBigMapAllowanceAsync(
            string holderAddress,
            string spenderAddress,
            CancellationToken cancellationToken = default)
        {
            if (_currency.Name == FA2)
            {
                var token = _currency as TezosTokens.FA2;

                try
                {
                    return await HttpHelper.GetAsyncResult<decimal>(
                        baseUri: token.BcdApi,
                        requestUri: $"bigmap/{token.BcdNetwork}/{token.TokenPointerAllowance}/keys?q={holderAddress}",
                        headers: _headers,
                        responseHandler: (response, content) =>
                        {
                            var bigMap = JsonConvert.DeserializeObject<JArray>(content);

                            if (bigMap.Count == 0)
                                return 0;

                            //todo: refactoring
                            if (bigMap[0]["data"]["value"].Contains("children"))
                                return (bigMap[0]["data"]?["value"]?["children"]?.Where(a => a["value"]?.Value<string>() == spenderAddress) != null) ? 1 : 0;
                            else
                                return 0;
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    return new Error(Errors.RequestError, e.Message);
                }
            }
            else
                return 0;
        }

        public async Task<Result<decimal>> TryGetTokenBigMapAllowanceAsync(
            string holderAddress,
            string spenderAddress,
            int attempts = 3,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTokenBigMapAllowanceAsync(holderAddress, spenderAddress, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting big map allowance after {attempts} attempts");
        }

        #endregion

        private JObject GetBalanceParams(string holderAddress, string viewContractAddress)
        {
            return JObject.Parse(@"{'entrypoint':'getBalance','value':{'args': [{'string':'" + holderAddress + "'},{'string':'" + viewContractAddress + "%viewNat'}],'prim': 'Pair'}}");
        }

        private JObject GetAllowanceParams(string holderAddress, string spenderAddress, string viewContractAddress)
        {
            return JObject.Parse(@"{'entrypoint':'getAllowance','value':{'args':[{'args':[{'string':'" + holderAddress + "'},{'string':'" + spenderAddress + "'}],'prim':'Pair'},{'string':'" + viewContractAddress + "%viewNat'}],'prim': 'Pair'}}");
        }

        public async Task<Result<IEnumerable<TezosTransaction>>> GetTransactionsAsync(
            string from,
            string to,
            string parameters,
            CancellationToken cancellationToken = default)
        {
            return await HttpHelper.GetAsyncResult(
                        baseUri: _baseUri,
                        requestUri: $"operations/transactions?sender={from}&target={to}&parameters.eq={parameters}",
                        headers: _headers,
                        responseHandler: (response, content) => ParseTxs(JsonConvert.DeserializeObject<JArray>(content), parseTokenParams: false),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
        }

        public async Task<Result<IEnumerable<TezosTransaction>>> TryGetTransactionsAsync(
            string from,
            string to,
            string parameters,
            int attempts = 10,
            int attemptsIntervalMs = 1000,
            CancellationToken cancellationToken = default)
        {
            return await ResultHelper.TryDo((c) => GetTransactionsAsync(from, to, parameters, c), attempts, attemptsIntervalMs, cancellationToken)
                .ConfigureAwait(false) ?? new Error(Errors.RequestError, $"Connection error while getting transactions after {attempts} attempts");
        }
    }
}
