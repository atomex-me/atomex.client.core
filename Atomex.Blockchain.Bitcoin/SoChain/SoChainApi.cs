using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NBitcoin;
using NBitcoinTransaction = NBitcoin.Transaction;
using Network = NBitcoin.Network;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.Bitcoin.Abstract;
using Atomex.Blockchain.Bitcoin.Common;
using Atomex.Common;
using Transaction = Atomex.Blockchain.Abstract.Transaction;

namespace Atomex.Blockchain.Bitcoin.SoChain
{
    public class SoChainSettings
    {
        public string BaseUri { get; set; }
        public string Network { get; set; }
        public int RequestLimitDelayMs { get; set; } = 1000;
    }

    public class SoChainApi : IBitcoinApi, IBlockchainSwapApi
    {
        public const string Uri = "https://sochain.com/";

        protected readonly string _currency;
        protected readonly SoChainSettings _settings;

        private static RequestLimitControl _rlcInstance;
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

        public SoChainApi(string currency, SoChainSettings settings)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #region IBlockchainApi

        public async Task<(decimal balance, Error error)> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/get_tx_unspent/{_settings.Network}/{address}";

            var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (balance: 0m, error: new Error((int)response.StatusCode, "Error status code received"));

            var data = JsonConvert.DeserializeObject<JObject>(content)?["data"];

            if (data is null)
                throw new Exception($"Invalid response data");

            if (data?["txs"] is not JArray txs || !txs.Any())
                return (balance: 0m, error: null);

            var result = 0m;

            foreach (var tx in txs)
            {
                var isConfirmed = tx["confirmations"]?.Value<long>() > 0;

                if (isConfirmed)
                    result += tx["value"]?.Value<decimal>() ?? 0;
            }

            return (balance: 0m, error: null);
        }

        public async Task<(Transaction tx, Error error)> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/tx/{_settings.Network}/{txId}";

            var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (tx: null, error: new Error((int)response.StatusCode, "Error status code received"));

            var tx = JsonConvert.DeserializeObject<JObject>(content)?["data"];

            if (tx == null)
                return (tx: null, error: new Error(Errors.GetTransactionError, "Null data received"));

            var confirmations = tx["confirmations"].Value<long>();

            var status = confirmations > 0
                ? TransactionStatus.Confirmed
                : TransactionStatus.Pending;

            var time = DateTimeOffset.FromUnixTimeSeconds(tx["time"].Value<int>()).UtcDateTime;

            var blockHeight = tx["block_no"].Type != JTokenType.Null
                ? tx["block_no"].Value<long>()
                : 0;

            var nativeTx = NBitcoinTransaction.Parse(
                hex: tx["tx_hex"].Value<string>(),
                network: ResolveNetwork(_currency, _settings.Network));

            return (
                tx: new BitcoinTransaction(
                    currency: _currency,
                    tx: nativeTx,
                    status: status,
                    creationTime: time,
                    blockTime: confirmations > 0
                        ? new DateTime?(time)
                        : null,
                    blockHeight: blockHeight,
                    confirmations: confirmations),
                error: null
            );
        }

        public async Task<(string txId, Error error)> BroadcastAsync(
            Transaction transaction,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/send_tx/{_settings.Network}";

            using var requestContent = new StringContent(
                content: JsonConvert.SerializeObject(new
                {
                    tx_hex = (transaction as BitcoinTransaction).ToHex(),
                    network = _settings.Network
                }),
                encoding: Encoding.UTF8,
                mediaType: "application/json");

            var response = await HttpHelper
                .PostAsync(
                    baseUri: _settings.BaseUri,
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
                return (txId: null, error: new Error((int)response.StatusCode, "Error status code received"));

            var txId = JsonConvert.DeserializeObject<JObject>(content)
                ?["data"]
                ?["txid"]
                ?.Value<string>();

            return (txId, error: null);
        }

        #endregion IBlockchainApi

        #region IBitcoinApi

        public async Task<(IEnumerable<BitcoinTxOutput> outputs, Error error)> GetOutputsAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/address/{_settings.Network}/{address}";

            var response = await HttpHelper
                .GetAsync(
                    baseUri: _settings.BaseUri,
                    relativeUri: requestUri,
                    requestLimitControl: GetRequestLimitControl(_settings.RequestLimitDelayMs),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var content = await response
                .Content
                .ReadAsStringAsync()
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (outputs: null, error: new Error((int)response.StatusCode, "Error status code received"));

            var data = JsonConvert.DeserializeObject<JObject>(content)?["data"];

            if (data is null)
                return (outputs: null, error: new Error(Errors.GetOutputsError, "Invalid response: data is null"));

            if (data?["txs"] is not JArray txs || !txs.Any())
                return (outputs: Enumerable.Empty<BitcoinTxOutput>(), error: null);

            var network = ResolveNetwork(_currency, _settings.Network);
            var outputs = new List<BitcoinTxOutput>();

            // parse incomings
            foreach (var tx in txs)
            {
                if (tx["incoming"] is not JObject o)
                    continue;

                var isConfirmed = tx["confirmations"]?.Value<long>() > 0;

                var coin = new Coin(
                    fromTxHash: uint256.Parse(tx["txid"]?.Value<string>()),
                    fromOutputIndex: o["output_no"]?.Value<uint>() ?? 0,
                    amount: new Money(o["value"]?.Value<decimal>() ?? 0, MoneyUnit.BTC).Satoshi,
                    scriptPubKey: Script.FromHex(o["script_hex"]?.Value<string>()));

                var spentTxId = o.SelectToken("spent.txid")?.Value<string>();

                var spentTxPoint = spentTxId != null
                    ? new BitcoinTxPoint
                    {
                        Hash = spentTxId,
                        Index = o.SelectToken("spent.input_no")?.Value<uint>() ?? 0
                    }
                    : null;

                outputs.Add(new BitcoinTxOutput
                {
                    Currency = _currency,
                    Coin = coin,
                    SpentTxPoints = spentTxPoint != null
                        ? new List<BitcoinTxPoint> { spentTxPoint }
                        : null,
                    Address = coin.GetAddressOrDefault(network),
                    IsConfirmed = isConfirmed,
                    IsSpentConfirmed = spentTxId != null // on sochain Spent is filled only in case of a confirmed transaction
                });
            }

            Dictionary<string, BitcoinTxOutput> outputsIndex = null;

            // parse unconfirmed outgoings
            foreach (var tx in txs)
            {
                var isConfirmed = tx["confirmations"]?.Value<long>() > 0;

                if (isConfirmed)
                    continue;

                if (tx["outgoing"] is not JObject o)
                    continue;

                if (outputsIndex == null)
                    outputsIndex = outputs.ToDictionary(output => $"{output.TxId}:{output.Index}", output => output);

                var txId = tx["txid"].Value<string>();
                var value = o["value"]?.Value<decimal>() ?? 0;

                var (unconfirmedTx, error) = await GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return (outputs: null, error);

                if (unconfirmedTx is not BitcoinTransaction btcUnconfirmedTx)
                    return (outputs: null, error: new Error(Errors.GetOutputsError, "Btc unconfirmed transaction is null"));

                foreach (var i in btcUnconfirmedTx.Inputs)
                {
                    if (outputsIndex.TryGetValue($"{i.PreviousOutput.Hash}:{i.PreviousOutput.Index}", out var output))
                    {
                        var spentPoint = new BitcoinTxPoint { Hash = txId, Index = i.Index };

                        if (output.SpentTxPoints == null)
                            output.SpentTxPoints = new List<BitcoinTxPoint> { spentPoint };
                        else
                            output.SpentTxPoints.Add(spentPoint);
                    }
                }
            }

            if (outputsIndex != null)
                outputsIndex.Clear();

            return (outputs, error: null);
        }

        #endregion IBitcoinApi

        #region IBlockchainSwapApi

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindLocksAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            return await BitcoinSwapHelper
                .FindLocksAsync(
                    bitcoinApi: this,
                    secretHash: secretHash,
                    address: address,
                    refundAddress: refundAddress,
                    timeStamp: timeStamp,
                    lockTime: lockTime,
                    secretSize: secretSize,
                    network: ResolveNetwork(_currency, _settings.Network),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public Task<(IEnumerable<Transaction> txs, Error error)> FindAdditionalLocksAsync(
            string secretHash,
            string contractAddress,
            ulong timeStamp,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            // there is no additional locks for bitcoin based currencies
            return Task.FromResult((txs: Enumerable.Empty<Transaction>(), error: (Error)null));
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindRedeemsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            return await BitcoinSwapHelper
                .FindRedeemsAsync(
                    bitcoinApi: this,
                    secretHash: secretHash,
                    address: address,
                    refundAddress: refundAddress,
                    timeStamp: timeStamp,
                    lockTime: lockTime,
                    secretSize: secretSize,
                    network: ResolveNetwork(_currency, _settings.Network),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<(IEnumerable<Transaction> txs, Error error)> FindRefundsAsync(
            string secretHash,
            string contractAddress,
            string address,
            string refundAddress,
            ulong timeStamp,
            ulong lockTime,
            int secretSize,
            string version = null,
            CancellationToken cancellationToken = default)
        {
            return await BitcoinSwapHelper
                .FindRefundsAsync(
                    bitcoinApi: this,
                    secretHash: secretHash,
                    address: address,
                    refundAddress: refundAddress,
                    timeStamp: timeStamp,
                    lockTime: lockTime,             
                    secretSize: secretSize,
                    network: ResolveNetwork(_currency, _settings.Network),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        #endregion IBlockchainSwapApi

        #region Common

        public static Network ResolveNetwork(string currency, string network)
        {
            return (currency, network) switch
            {
                ("BTC", "BTC")       => Network.Main,
                ("BTC", "BTCTEST")   => Network.TestNet,
                ("LTC", "LTC")       => NBitcoin.Altcoins.Litecoin.Instance.Mainnet,
                ("LTC", "LTCTEST")   => NBitcoin.Altcoins.Litecoin.Instance.Testnet,
                ("DOGE", "DOGE")     => NBitcoin.Altcoins.Dogecoin.Instance.Mainnet,
                ("DOGE", "DOGETEST") => NBitcoin.Altcoins.Dogecoin.Instance.Testnet,
                ("DASH", "DASH")     => NBitcoin.Altcoins.Dash.Instance.Mainnet,
                ("DASH", "DASHTEST") => NBitcoin.Altcoins.Dash.Instance.Testnet,
                _ => throw new NotSupportedException($"Currency {currency} with network {network} not supporeted.")
            };
        }

        #endregion Common
    }
}