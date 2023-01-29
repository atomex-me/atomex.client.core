using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Common;

namespace Atomex.Blockchain.BlockCypher
{
    public class BlockCypherSettings
    {
        public string BaseUrl { get; set; } = "https://api.blockcypher.com/v1/";
        public string? ApiToken { get; set; }
        public string Coin { get; set; }
        public string Network { get; set; }
        public int RequestLimitDelayMs { get; set; } = 1000;
        public int Decimals { get; set; } = 8;
    }

    public class BlockCypherApi : BitcoinBlockchainApi
    {
        protected readonly string _currency;
        protected readonly BlockCypherSettings _settings;
        protected readonly ILogger? _logger;

        private static RequestLimitControl? _rlcInstance;
        private static RequestLimitControl GetRequestLimitControl(int delayMs)
        {
            var instance = _rlcInstance;

            if (instance == null)
            {
                Interlocked.CompareExchange(ref _rlcInstance, new RequestLimitControl(delayMs), null);
                instance = _rlcInstance;
            }

            return instance;
        }

        public BlockCypherApi(string currency, BlockCypherSettings settings, ILogger? logger = null)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
        }

        public override async Task<Result<string>> BroadcastAsync(
            BitcoinTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var txHex = tx.ToBytes().ToHexString();

            var requestUri = $"/{_settings.Coin}/{_settings.Network}/txs/push" + (_settings.ApiToken != null ? $"?token={_settings.ApiToken}" : "");

            using var requestContent = new StringContent(
                content: $"{{\"tx\":\"{txHex}\"}}",
                encoding: Encoding.UTF8,
                mediaType: "application/json");

            var response = await HttpHelper
                .PostAsync(
                    baseUri: _settings.BaseUrl,
                    relativeUri: requestUri,
                    content: requestContent,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var txResponse = JsonConvert.DeserializeObject<JObject>(content);

            if (txResponse == null)
                return new Result<string> { Value = null };

            return new Result<string>
            {
                Value = txResponse["tx"] is JObject txData && txData.ContainsKey("hash")
                    ? txData.Value<string>("hash")
                    : null
            };
        }

        public override async Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"/{_settings.Coin}/{_settings.Network}/addrs/{address}/balance" + (_settings.ApiToken != null ? $"?token={_settings.ApiToken}" : "");

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUrl,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var balanceResponse = JsonConvert.DeserializeObject<JObject>(content);

            if (balanceResponse == null || !balanceResponse.ContainsKey("final_balance"))
                return new Error(Errors.InvalidResponse, "Balance getting error. Invalid response format");

            var balanceInSatoshi = balanceResponse.Value<long>("final_balance");

            return (BigInteger)balanceInSatoshi;
        }

        public override async Task<Result<BitcoinTxInput>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default)
        {
            var (tx, error) = await GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (tx is not BitcoinTransaction btcBasedTx)
                return new Error(Errors.GetInputError, "Input is null");

            if (inputNo >= btcBasedTx.Inputs.Length)
                return new Error(Errors.InvalidResponse, "Invalid input number");

            return new Result<BitcoinTxInput>
            {
                Value = btcBasedTx.Inputs[inputNo]
            };
        }

        public override async Task<Result<IEnumerable<BitcoinTxOutput>>> GetOutputsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (addressInfo, error) = await GetAddressInfo(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            // todo: check addressInfo == null

            return new Result<IEnumerable<BitcoinTxOutput>>
            {
                Value = addressInfo?.Outputs
            };
        }

        public override async Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"/{_settings.Coin}/{_settings.Network}/txs/{txId}?includeHex=true&instart=0&outstart=0&limit=1000" + (_settings.ApiToken != null ? $"&token={_settings.ApiToken}" : "");

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUrl,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var tx = JsonConvert.DeserializeObject<JObject>(content, new JsonSerializerSettings()
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            });

            var txHex = tx.ContainsKey("hex")
                ? tx.Value<string>("hex")
                : null;

            if (txHex == null)
                return new Error(Errors.InvalidResponse, "Invalid tx response.");

            return new BitcoinTransaction(
                currency: _currency,
                tx: Transaction.Parse(txHex, ResolveNetwork(_currency, _settings.Coin, _settings.Network)),
                creationTime: tx.ContainsKey("received") ? new DateTimeOffset?(tx.Value<DateTime>("received")) : null,
                blockTime: tx.ContainsKey("confirmed") ? new DateTimeOffset?(tx.Value<DateTime>("confirmed")) : null,
                blockHeight: tx.ContainsKey("block_height") ? tx.Value<long>("block_height") : 0,
                confirmations: tx.ContainsKey("confirmations") ? tx.Value<int>("confirmations") : 0,
                fee: tx.ContainsKey("fees") ? tx.Value<long>("fees") : 0
            );
        }

        public override async Task<Result<IEnumerable<BitcoinTxOutput>>> GetUnspentOutputsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (outputs, error) = await GetOutputsAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return outputs
                .Where(o => !o.IsSpent)
                .ToList();
        }

        public override async Task<Result<BitcoinTxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"/{_settings.Coin}/{_settings.Network}/txs/{txId}" + (_settings.ApiToken != null ? $"?token={_settings.ApiToken}" : "");

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUrl,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var tx = JsonConvert.DeserializeObject<JObject>(content, new JsonSerializerSettings()
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            });

            var outputs = tx["outputs"] as JArray;

            if (outputNo >= outputs.Count)
                return new Error(Errors.InvalidResponse, $"There is no output with no {outputNo}");

            var spentTxId = outputs[(int)outputNo]?.Value<string>("spent_by");

            if (spentTxId == null)
                return new Result<BitcoinTxPoint> { Value = null };

            var (spentTx, error) = await GetTransactionAsync(spentTxId, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (spentTx == null)
                return new Result<BitcoinTxPoint> { Value = null };

            var spentBtcTx = spentTx as BitcoinTransaction;

            foreach (var input in spentBtcTx.Inputs)
            {
                if (input.PreviousOutput.Index == outputNo && input.PreviousOutput.Hash == txId)
                {
                    return new BitcoinTxPoint
                    {
                        Index = input.Index,
                        Hash = spentTxId
                    };
                }
            }

            return new Result<BitcoinTxPoint> { Value = null };
        }

        public override async Task<Result<BitcoinAddressInfo>> GetAddressInfo(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"/{_settings.Coin}/{_settings.Network}/addrs/{address}/full?txlimit=1000" + (_settings.ApiToken != null ? $"&token={_settings.ApiToken}" : "");

            using var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUrl,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new Error((int)response.StatusCode, "Error status code received");

            var addr = JsonConvert.DeserializeObject<JObject>(content, new JsonSerializerSettings()
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            });

            var balanceInSatoshi = addr["balance"].Value<long>();
            var receivedInSatoshi = addr["total_received"].Value<long>();
            var sentInSatoshi = addr["total_sent"].Value<long>();
            var unconfirmedIncomeInSatoshi = 0L;
            var unconfirmedOutcomeInSatoshi = 0L;
            //var unconfirmedBalanceInSatoshi = addr["unconfirmed_balance"].Value<long>();

            if (!addr.ContainsKey("txs"))
            {
                return new BitcoinAddressInfo(
                    Balance: balanceInSatoshi.ToDecimal(_settings.Decimals),
                    Received: receivedInSatoshi.ToDecimal(_settings.Decimals),
                    Sent: sentInSatoshi.ToDecimal(_settings.Decimals),
                    UnconfirmedIncome: 0,
                    UnconfirmedOutcome: 0,
                    Outputs: Enumerable.Empty<BitcoinTxOutput>());
            }

            var txs = addr["txs"] as JArray;

            var outputs = new List<BitcoinTxOutput>();
            var outgoingTxConfirmations = new Dictionary<string, long>();
            var unresolvedSpentTxConfirmations = new Dictionary<(string, uint), BitcoinTxOutput>();

            foreach (var tx in txs.Cast<JObject>())
            {
                if (tx.ContainsKey("inputs"))
                {
                    var txInputs = tx["inputs"] as JArray;

                    foreach (var txInput in txInputs.Cast<JObject>())
                    {
                        var addresses = txInput.ContainsKey("addresses")
                            ? txInput["addresses"] as JArray
                            : null;

                        if (addresses == null || !addresses.Values<string>().Contains(address))
                            continue;

                        outgoingTxConfirmations.Add(tx.Value<string>("hash"), tx.Value<long>("confirmations"));
                    }
                }

                if (tx.ContainsKey("outputs"))
                {
                    var txOutputs = tx["outputs"] as JArray;

                    var txOutputN = 0u;

                    foreach (var txOutput in txOutputs.Cast<JObject>())
                    {
                        var addresses = txOutput.ContainsKey("addresses")
                            ? txOutput["addresses"] as JArray
                            : null;

                        if (addresses == null)
                        {
                            txOutputN++;
                            continue;
                        }

                        if (addresses.Count != 1 || !addresses.Values<string>().Contains(address))
                        {
                            txOutputN++;
                            continue;
                        }

                        var txHash = tx.Value<string>("hash");
                        var amount = new Money(txOutput.Value<long>("value"), MoneyUnit.Satoshi);
                        var script = Script.FromHex(txOutput.Value<string>("script"));

                        var spentTxPoint = txOutput.ContainsKey("spent_by")
                            ? new BitcoinTxPoint
                            {
                                Index = 0,
                                Hash = txOutput.Value<string>("spent_by")
                            }
                            : null;

                        var spentTxConfirmations = 0L;
                        var spentTxResolved = true;

                        if (spentTxPoint != null && !outgoingTxConfirmations.TryGetValue(spentTxPoint.Hash, out spentTxConfirmations))
                        {
                            spentTxConfirmations = 0;
                            spentTxResolved = false;
                        }

                        var output = new BitcoinTxOutput
                        {
                            Currency = _currency,
                            Coin = new Coin(
                                fromTxHash: new uint256(txHash),
                                fromOutputIndex: txOutputN,
                                amount: amount,
                                scriptPubKey: script),
                            IsConfirmed = tx.Value<long>("confirmations") > 0,
                            SpentTxPoints = spentTxPoint != null
                                ? new List<BitcoinTxPoint> { spentTxPoint }
                                : null,
                            IsSpentConfirmed = spentTxConfirmations > 0
                        };

                        outputs.Add(output);

                        if (!spentTxResolved)
                            unresolvedSpentTxConfirmations.Add((txHash, txOutputN), output);

                        txOutputN++;
                    }
                }
            }

            // try resolve unresolved spent tx
            if (unresolvedSpentTxConfirmations.Any())
            {
                foreach (var tx in unresolvedSpentTxConfirmations)
                {
                    foreach (var spentTx in tx.Value.SpentTxPoints!)
                    {
                        if (!outgoingTxConfirmations.TryGetValue(spentTx.Hash, out var spentTxConfirmations))
                        {
                            _logger?.LogWarning("[BlockCypherApi] Can't find confirmations info for spent tx {@hash}", spentTx.Hash);
                            continue;
                        }

                        if (spentTxConfirmations > 0)
                        {
                            tx.Value.IsSpentConfirmed = true;
                            break;
                        }
                    }
                }
            }

            foreach (var output in outputs)
            {
                // unconfirmed income
                if (!output.IsConfirmed)
                    unconfirmedIncomeInSatoshi += output.Value;

                // unconfirmed outcome
                if (output.IsSpent && !output.IsSpentConfirmed)
                    unconfirmedOutcomeInSatoshi += output.Value;
            }

            return new BitcoinAddressInfo(
                Balance: balanceInSatoshi.ToDecimal(_settings.Decimals),
                Received: receivedInSatoshi.ToDecimal(_settings.Decimals),
                Sent: sentInSatoshi.ToDecimal(_settings.Decimals),
                UnconfirmedIncome: unconfirmedIncomeInSatoshi.ToDecimal(_settings.Decimals),
                UnconfirmedOutcome: unconfirmedOutcomeInSatoshi.ToDecimal(_settings.Decimals),
                Outputs: outputs);
        }

        public static Network ResolveNetwork(string currency, string coin, string network)
        {
            return (currency, coin, network) switch
            {
                ("BTC", "btc", "main") => Network.Main,
                ("BTC", "btc", "test3") => Network.TestNet,
                ("LTC", "ltc", "main") => NBitcoin.Altcoins.Litecoin.Instance.Mainnet,
                ("DOGE", "doge", "main") => NBitcoin.Altcoins.Dogecoin.Instance.Mainnet,
                ("DASH", "dash", "main") => NBitcoin.Altcoins.Dash.Instance.Mainnet,
                _ => throw new NotSupportedException($"Currency {currency} with coin {coin} and network {network} not supporeted")
            };
        }
    }
}