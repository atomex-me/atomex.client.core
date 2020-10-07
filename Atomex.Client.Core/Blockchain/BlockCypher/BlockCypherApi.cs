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

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.BlockCypher
{
    public class BlockCypherApi : BitcoinBasedBlockchainApi
    {
        public const string BitcoinMainNet = "https://api.blockcypher.com/v1/btc/main";

        private const int MinDelayBetweenRequestMs = 1000;
        private string BaseUri { get; }

        private static readonly RequestLimitControl RequestLimitControl
            = new RequestLimitControl(MinDelayBetweenRequestMs);

        public BitcoinBasedCurrency Currency { get; }

        public BlockCypherApi(BitcoinBasedCurrency currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = baseUri;
        }

        public BlockCypherApi(BitcoinBasedCurrency currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            BaseUri = configuration["BlockchainApiBaseUri"];
        }

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var tx = (IBitcoinBasedTransaction)transaction;
            var txHex = tx.ToBytes().ToHexString();

            tx.State = BlockchainTransactionState.Pending;

            const string requestUri = "/txs/push";
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
                            return null;

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
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/addrs/{address}/balance";

            return await HttpHelper.GetAsyncResult<decimal>(
                    baseUri: BaseUri,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var balanceResponse = JsonConvert.DeserializeObject<JObject>(content);

                        if (balanceResponse == null && !balanceResponse.ContainsKey("final_balance"))
                            return new Error(Errors.InvalidResponse, "Balance getting error. Invalid response format.");

                        return balanceResponse.Value<long>("final_balance");
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
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var tx = await GetTransactionAsync(txId, cancellationToken)
                .ConfigureAwait(false);

            if (tx.HasError)
                return tx.Error;

            var btcBasedTx = tx.Value as BitcoinBasedTransaction;

            if (inputNo >= btcBasedTx.Inputs.Length)
                return new Error(Errors.InvalidResponse, "Invalid input number.");

            return new Result<ITxPoint>(btcBasedTx.Inputs[inputNo]);
        }

        public override async Task<Result<IEnumerable<ITxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/addrs/{address}/full?txlimit=1000";

            return await HttpHelper.GetAsyncResult(
                baseUri: BaseUri,
                requestUri: requestUri,
                responseHandler: (response, content) =>
                {
                    var addr = JsonConvert.DeserializeObject<JObject>(content, new JsonSerializerSettings()
                    {
                        DateTimeZoneHandling = DateTimeZoneHandling.Utc
                    });

                    if (!addr.ContainsKey("txs"))
                        return new Result<IEnumerable<ITxOutput>>(Enumerable.Empty<ITxOutput>());

                    var txs = addr["txs"] as JArray;

                    var result = new List<BitcoinBasedTxOutput>();

                    foreach (var tx in txs.Cast<JObject>())
                    {
                        if (!tx.ContainsKey("outputs"))
                            continue;

                        var outputs = tx["outputs"] as JArray;

                        var outputN = 0u;

                        foreach (var output in outputs.Cast<JObject>())
                        {
                            var addresses = output.ContainsKey("addresses")
                                ? output["addresses"] as JArray
                                : null;

                            if (addresses == null)
                            {
                                outputN++;
                                continue;
                            }

                            if (addresses.Count != 1 || !addresses.Values<string>().Contains(address))
                            {
                                outputN++;
                                continue;
                            }

                            var amount = new Money(output.Value<long>("value"), MoneyUnit.Satoshi);
                            var script = Script.FromHex(output.Value<string>("script"));

                            var spentTxPoint = output.ContainsKey("spent_by")
                                ? new TxPoint(0, output.Value<string>("spent_by"))
                                : null;

                            result.Add(new BitcoinBasedTxOutput(
                                coin: new Coin(
                                    fromTxHash: new uint256(tx.Value<string>("hash")),
                                    fromOutputIndex: outputN,
                                    amount: amount,
                                    scriptPubKey: script),
                                spentTxPoint: spentTxPoint));

                            outputN++;
                        }
                    }

                    return result;
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/txs/{txId}?includeHex=true&instart=0&outstart=0&limit=1000";

            return await HttpHelper.GetAsyncResult<IBlockchainTransaction>(
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

                    return new BitcoinBasedTransaction(
                        currency: Currency,
                        tx: Transaction.Parse(txHex, Currency.Network),
                        blockInfo: new BlockInfo
                        {
                            Confirmations = tx.ContainsKey("confirmations") ? tx.Value<int>("confirmations") : 0,
                            BlockHash     = tx.ContainsKey("block_hash") ? tx.Value<string>("block_hash") : null,
                            BlockHeight   = tx.ContainsKey("block_height") ? tx.Value<long>("block_height") : 0,
                            BlockTime     = tx.ContainsKey("confirmed") ? new DateTime?(tx.Value<DateTime>("confirmed")) : null,
                            FirstSeen     = tx.ContainsKey("received") ? new DateTime?(tx.Value<DateTime>("received")) : null,
                        },
                        fees: tx.ContainsKey("fees") ? tx.Value<long>("fees") : 0
                    );
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        }

        public override async Task<Result<IEnumerable<ITxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            var outputsResult = await GetOutputsAsync(address, afterTxId, cancellationToken)
                .ConfigureAwait(false);

            if (outputsResult.HasError)
                return outputsResult.Error;

            return outputsResult
                .Value
                .Where(o => o.SpentTxPoint == null)
                .ToList();
        }

        public override async Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default)
        {
            await RequestLimitControl
                .Wait(cancellationToken)
                .ConfigureAwait(false);

            var requestUri = $"/txs/{txId}";

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

            if (spentResult.HasError)
                return spentResult.Error;

            if (spentResult == null || spentResult.Value == null)
                return new Result<ITxPoint>((ITxPoint)null);

            var spentTxResult = await GetTransactionAsync(spentResult.Value, cancellationToken)
                .ConfigureAwait(false);

            if (spentTxResult.HasError)
                return spentTxResult.Error;

            var spentTx = spentTxResult.Value as BitcoinBasedTransaction;

            for (var i = 0; i < spentTx.Inputs.Length; ++i)
                if (spentTx.Inputs[i].Index == outputNo && spentTx.Inputs[i].Hash == txId)
                    return new TxPoint((uint)i, spentResult.Value);

            return new Result<ITxPoint>((ITxPoint)null);
        }
    }
}