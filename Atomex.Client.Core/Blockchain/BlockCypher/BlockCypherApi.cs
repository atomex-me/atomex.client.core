using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin;
using Atomex.Common;

namespace Atomex.Blockchain.BlockCypher
{
    public class BlockCypherApi : BitcoinBlockchainApi
    {
        public const string BitcoinMainNet = "https://api.blockcypher.com/v1/btc/main";

        private const int MinDelayBetweenRequestMs = 1000;
        private string BaseUri { get; }
        private string ApiToken { get; }

        private static readonly RequestLimitControl RequestLimitControl
            = new(MinDelayBetweenRequestMs);

        public BitcoinBasedConfig Currency { get; }

        public BlockCypherApi(BitcoinBasedConfig currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri  = baseUri;
        }

        public BlockCypherApi(BitcoinBasedConfig currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri  = configuration["BlockchainApiBaseUri"];
            ApiToken = configuration["BlockchainApiToken"];
        }

        public override async Task<Result<string>> BroadcastAsync(
            ITransaction transaction,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var tx = (BitcoinTransaction)transaction;
            var txHex = tx.ToBytes().ToHexString();

            var requestUri = "/txs/push" + (ApiToken != null ? $"?token={ApiToken}" : "");
            using var requestContent = new StringContent(
                content: $"{{\"tx\":\"{txHex}\"}}",
                encoding: Encoding.UTF8,
                mediaType: "application/json");

            return await HttpHelper.PostAsyncResult<string>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    content: requestContent,
                    responseHandler: (response, responseContent) =>
                    {
                        var txResponse = JsonConvert.DeserializeObject<JObject>(responseContent);

                        if (txResponse == null)
                            return new Result<string> { Value = null };

                        return txResponse["tx"] is JObject tx && tx.ContainsKey("hash")
                            ? tx.Value<string>("hash")
                            : null;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/addrs/{address}/balance" + (ApiToken != null ? $"?token={ApiToken}" : "");

            return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var balanceResponse = JsonConvert.DeserializeObject<JObject>(content);

                        if (balanceResponse == null && !balanceResponse.ContainsKey("final_balance"))
                            return new Error(Errors.InvalidResponse, "Balance getting error. Invalid response format.");

                        var balanceInSatoshi = balanceResponse.Value<long>("final_balance");

                        return Currency.SatoshiToCoin(balanceInSatoshi);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var (tx, error) = await GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            var btcBasedTx = tx as BitcoinTransaction;

            if (inputNo >= btcBasedTx.Inputs.Length)
                return new Error(Errors.InvalidResponse, "Invalid input number");

            return new Result<ITxPoint> { Value = btcBasedTx.Inputs[inputNo] };
        }

        public override async Task<Result<IEnumerable<BitcoinTxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            var (addressInfo, error) = await GetAddressInfo(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            // todo: check addressInfo == null

            return new Result<IEnumerable<BitcoinTxOutput>> { Value = addressInfo?.Outputs };
        }

        public override async Task<Result<ITransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/txs/{txId}?includeHex=true&instart=0&outstart=0&limit=1000" + (ApiToken != null ? $"&token={ApiToken}" : "");

            return await HttpHelper.GetAsyncResult<ITransaction>(
                baseUri: BaseUri,
                requestUri: requestUri,
                responseHandler: (response, content) =>
                {
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
                        currency: Currency.Name,
                        tx: Transaction.Parse(txHex, Currency.Network),
                        creationTime: tx.ContainsKey("received") ? new DateTime?(tx.Value<DateTime>("received")) : null,
                        blockTime: tx.ContainsKey("confirmed") ? new DateTime?(tx.Value<DateTime>("confirmed")) : null,
                        blockHeight: tx.ContainsKey("block_height") ? tx.Value<long>("block_height") : 0,
                        confirmations: tx.ContainsKey("confirmations") ? tx.Value<int>("confirmations") : 0,
                        fee: tx.ContainsKey("fees") ? tx.Value<long>("fees") : 0
                    );
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        }

        public override async Task<Result<IEnumerable<BitcoinTxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            var (outputs, error) = await GetOutputsAsync(address, afterTxId, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            return outputs
                .Where(o => o.SpentTxPoint == null)
                .ToList();
        }

        public override async Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/txs/{txId}" + (ApiToken != null ? $"?token={ApiToken}" : "");

            var spentResult = await HttpHelper.GetAsyncResult<string>(
                baseUri: BaseUri,
                requestUri: requestUri,
                responseHandler: (response, content) =>
                {
                    var tx = JsonConvert.DeserializeObject<JObject>(content, new JsonSerializerSettings()
                    {
                        DateTimeZoneHandling = DateTimeZoneHandling.Utc
                    });

                    var outputs = tx["outputs"] as JArray;

                    if (outputNo >= outputs.Count)
                        return new Error(Errors.InvalidResponse, $"There is no output with no {outputNo}");

                    var output = outputs[(int)outputNo];

                    return output?.Value<string>("spent_by");
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

            if (spentResult != null && spentResult.HasError)
                return spentResult.Error;

            if (spentResult == null || spentResult.Value == null)
                return new Result<ITxPoint>((ITxPoint)null);

            var spentTxResult = await GetTransactionAsync(spentResult.Value, cancellationToken)
                .ConfigureAwait(false);

            if (spentTxResult != null && spentTxResult.HasError)
                return spentTxResult.Error;

            if (spentTxResult == null || spentTxResult.Value == null)
                return new Result<ITxPoint>((ITxPoint)null);

            var spentTx = spentTxResult.Value as BitcoinTransaction;

            for (var i = 0; i < spentTx.Inputs.Length; ++i)
                if (spentTx.Inputs[i].Index == outputNo && spentTx.Inputs[i].Hash == txId)
                    return new TxPoint((uint)i, spentResult.Value);

            return new Result<ITxPoint>((ITxPoint)null);
        }

        public override async Task<Result<BitcoinAddressInfo>> GetAddressInfo(
            string address,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/addrs/{address}/full?txlimit=1000" + (ApiToken != null ? $"&token={ApiToken}" : "");

            return await HttpHelper.GetAsyncResult<BitcoinAddressInfo>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
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
                                Balance: Currency.SatoshiToCoin(balanceInSatoshi),
                                Received: Currency.SatoshiToCoin(receivedInSatoshi),
                                Sent: Currency.SatoshiToCoin(sentInSatoshi),
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
                                        ? new TxPoint(0, txOutput.Value<string>("spent_by"))
                                        : null;

                                    var spentTxConfirmations = 0L;
                                    var spentTxResolved = true;

                                    if (spentTxPoint != null && !outgoingTxConfirmations.TryGetValue(spentTxPoint.Hash, out spentTxConfirmations))
                                    {
                                        spentTxConfirmations = 0;
                                        spentTxResolved = false;
                                    }

                                    var output = new BitcoinTxOutput(
                                        coin: new Coin(
                                            fromTxHash: new uint256(txHash),
                                            fromOutputIndex: txOutputN,
                                            amount: amount,
                                            scriptPubKey: script),
                                        confirmations: tx.Value<long>("confirmations"),
                                        spentTxPoint: spentTxPoint,
                                        spentTxConfirmations: spentTxConfirmations);

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
                                if (!outgoingTxConfirmations.TryGetValue(tx.Value.SpentTxPoint.Hash, out var spentTxConfirmations))
                                {
                                    Log.Warning("[BlockCypherApi] Can't find confirmations info for spent tx {@hash}", tx.Value.SpentTxPoint.Hash);
                                    continue;
                                }

                                tx.Value.SpentTxConfirmations = spentTxConfirmations;
                            }
                        }

                        foreach (var output in outputs)
                        {
                            // unconfirmed income
                            if (output.Confirmations == 0)
                                unconfirmedIncomeInSatoshi += output.Value;

                            // unconfirmed outcome
                            if (output.SpentTxPoint != null && output.SpentTxConfirmations == 0)
                                unconfirmedOutcomeInSatoshi += output.Value;
                        }

                        return new BitcoinAddressInfo(
                            Balance: Currency.SatoshiToCoin(balanceInSatoshi),
                            Received: Currency.SatoshiToCoin(receivedInSatoshi),
                            Sent: Currency.SatoshiToCoin(sentInSatoshi),
                            UnconfirmedIncome: Currency.SatoshiToCoin(unconfirmedIncomeInSatoshi),
                            UnconfirmedOutcome: Currency.SatoshiToCoin(unconfirmedOutcomeInSatoshi),
                            Outputs: outputs);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}