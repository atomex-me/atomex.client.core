using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using NBitcoin;
using Newtonsoft.Json;
using NBitcoinTransaction = NBitcoin.Transaction;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin.SoChain
{
    public class SoChainSettings
    {
        public string BaseUri { get; set; } = "https://sochain.com/";
        public string Network { get; set; }
        public int RequestLimitDelayMs { get; set; } = 1000;
        public int Decimals { get; set; } = 8;
    }

    public partial class SoChainApi : BitcoinBlockchainApi
    {
        protected readonly string _currency;
        protected readonly SoChainSettings _settings;
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

        public SoChainApi(string currency, SoChainSettings settings, ILogger? logger = null)
        {
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
        }

        public override async Task<Result<BigInteger>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var (outputs, error) = await GetUnspentOutputsAsync(address, cancellationToken)
                .ConfigureAwait(false);

            if (error != null)
                return error;

            if (outputs == null)
                return BigInteger.Zero;

            var balanceInSatoshi = outputs.Sum(o => o.Value);

            return (BigInteger)balanceInSatoshi;
        }

        public override async Task<Result<BitcoinTxInput>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/get_tx_inputs/{_settings.Network}/{txId}/{inputNo}";

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
                return new Error((int)response.StatusCode, "Error status code received");

            var input = JsonConvert.DeserializeObject<Response<TxSingleInput>>(content)?.Data?.Inputs;

            if (input == null)
                return new Error(Errors.GetInputError, "Input is null");

            var witScript = WitScript.Empty;

            if (input.Witness != null)
                witScript = input.Witness.Aggregate(witScript, (current, witness) => current + new WitScript(witness));

            var script = input.Script != null
                ? ParseScript(input.Script)
                : Script.Empty;

            var scriptHex = script.ToHex();

            return new BitcoinTxInput
            {
                Index = input.InputNo,
                PreviousOutput = new BitcoinTxPoint
                {
                    Index = (uint)input.FromOutput.OutputNo,
                    Hash = input.FromOutput.TxId
                },
                ScriptSig = scriptHex,
                WitScript = witScript.ToBytes().ToHexString()
            };
        }

        public override async Task<Result<IEnumerable<BitcoinTxOutput>>> GetUnspentOutputsAsync(
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
                return new Error((int)response.StatusCode, "Error status code received");

            var outputs = JsonConvert.DeserializeObject<Response<AddressOutputs>>(content);

            return new Result<IEnumerable<BitcoinTxOutput>>
            {
                Value = outputs?.Data.Txs.Select(o => new BitcoinTxOutput
                {
                    Coin = new Coin(
                        fromTxHash: new uint256(o.TxId),
                        fromOutputIndex: (uint)o.OutputNo,
                        amount: new Money(
                        amount: decimal.Parse(o.Value, CultureInfo.InvariantCulture),
                        unit: MoneyUnit.BTC),
                        scriptPubKey: Script.FromHex(o.ScriptHex)),
                    Currency = _currency,
                    SpentTxPoints = null,
                    IsConfirmed = o.Confirmations > 0,
                    IsSpentConfirmed = false
                })
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

            return new Result<IEnumerable<BitcoinTxOutput>> { Value = addressInfo?.Outputs };
        }

        public override async Task<Result<ITransaction>> GetTransactionAsync(
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
                return new Error((int)response.StatusCode, "Error status code received");

            var responseData = JsonConvert.DeserializeObject<Response<Tx>>(content);

            var tx = responseData?.Data;

            if (tx == null)
                return new Error(Errors.GetTransactionError, "Null data received");

            var time = DateTimeOffset.FromUnixTimeSeconds(tx.Time).UtcDateTime;

            var blockHeight = tx.BlockNo != null
                ? tx.BlockNo.Value
                : 0;

            var nativeTx = NBitcoinTransaction.Parse(
                hex: tx.TxHex,
                network: ResolveNetwork(_currency, _settings.Network));

            var fee = decimal
                .Parse(tx.Fee, CultureInfo.InvariantCulture)
                .ToBigInteger(_settings.Decimals);

            return new BitcoinTransaction(
                currency: _currency,
                tx: nativeTx,
                creationTime: time,
                blockTime: tx.Confirmations > 0
                    ? new DateTime?(time)
                    : null,
                blockHeight: blockHeight,
                confirmations: tx.Confirmations,
                fee: fee);
        }

        public override async Task<Result<BitcoinTxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/is_tx_spent/{_settings.Network}/{txId}/{outputNo}";

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
                return new Error((int)response.StatusCode, "Error status code received");

            var info = JsonConvert.DeserializeObject<Response<TxOutputSpentInfo>>(content);

            return new Result<BitcoinTxPoint>
            {
                Value = info?.Data?.IsSpent ?? false
                    ? new BitcoinTxPoint
                    {
                        Index = info.Data.Spent.InputNo,
                        Hash = info.Data.Spent.TxId
                    }
                    : null
            };
        }

        public override async Task<Result<string>> BroadcastAsync(
            BitcoinTransaction tx,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/send_tx/{_settings.Network}";
            var txHex = tx.ToHex();

            _logger?.LogDebug("TxHex: {@txHex}", txHex);

            using var requestContent = new StringContent(
                content: JsonConvert.SerializeObject(new SendTx(txHex, _settings.Network)),
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
                return new Error((int)response.StatusCode, "Error status code received");

            var txId = JsonConvert.DeserializeObject<Response<SendTxId>>(content)?.Data?.TxId;

            if (txId == null)
                return new Error(Errors.TransactionBroadcastError, "Transaction id is null");

            return txId;
        }

        public override async Task<Result<BitcoinAddressInfo>> GetAddressInfo(
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
                return new Error((int)response.StatusCode, "Error status code received");

            var responseData = JsonConvert.DeserializeObject<Response<AddressDisplayData>>(content);

            if (responseData is null)
                return new Error(Errors.GetOutputsError, "Invalid response: data is null");

            var network = ResolveNetwork(_currency, _settings.Network);
            var outputs = new List<BitcoinTxOutput>();

            // parse incomings
            foreach (var tx in responseData.Data.Txs)
            {
                if (tx.Incoming == null)
                    continue;

                var isConfirmed = tx.Confirmations > 0;

                var coin = new Coin(
                    fromTxHash: uint256.Parse(tx.TxId),
                    fromOutputIndex: tx.Incoming.OutputNo,
                    amount: new Money(decimal.Parse(tx.Incoming.Value, CultureInfo.InvariantCulture), MoneyUnit.BTC).Satoshi,
                    scriptPubKey: Script.FromHex(tx.Incoming.ScriptHex));

                var spentTxId = tx.Incoming.Spent?.TxId;

                var spentTxPoint = spentTxId != null
                    ? new BitcoinTxPoint
                    {
                        Hash = spentTxId,
                        Index = tx.Incoming.Spent!.InputNo
                    }
                    : null;

                outputs.Add(new BitcoinTxOutput
                {
                    Currency = _currency,
                    Coin = coin,
                    SpentTxPoints = spentTxPoint != null
                        ? new List<BitcoinTxPoint> { spentTxPoint }
                        : null,
                    IsConfirmed = isConfirmed,
                    IsSpentConfirmed = spentTxId != null // on sochain Spent is filled only in case of a confirmed transaction
                });
            }

            Dictionary<string, BitcoinTxOutput>? outputsIndex = null;

            // parse unconfirmed outgoings
            foreach (var tx in responseData.Data.Txs)
            {
                var isConfirmed = tx.Confirmations > 0;

                if (isConfirmed)
                    continue;

                if (tx.Outgoing == null)
                    continue;

                outputsIndex ??= outputs.ToDictionary(output => $"{output.TxId}:{output.Index}", output => output);

                var txId = tx.TxId;
                var value = tx.Outgoing.Value;

                var (unconfirmedTx, error) = await GetTransactionAsync(txId, cancellationToken)
                    .ConfigureAwait(false);

                if (error != null)
                    return error;

                if (unconfirmedTx is not BitcoinTransaction btcUnconfirmedTx)
                    return new Error(Errors.GetOutputsError, "Btc unconfirmed transaction is null");

                foreach (var i in btcUnconfirmedTx.Inputs)
                {
                    if (outputsIndex.TryGetValue($"{i.PreviousOutput.Hash}:{i.PreviousOutput.Index}", out var output))
                    {
                        var spentPoint = new BitcoinTxPoint { Hash = txId, Index = i.Index };

                        if (output.SpentTxPoints == null)
                        {
                            output.SpentTxPoints = new List<BitcoinTxPoint> { spentPoint };
                        }
                        else
                        {
                            output.SpentTxPoints.Add(spentPoint);
                        }
                    }
                }
            }

            outputsIndex?.Clear();

            var balance = decimal.Parse(responseData.Data.Balance, CultureInfo.InvariantCulture);
            var received = decimal.Parse(responseData.Data.ReceivedValue, CultureInfo.InvariantCulture);
            var unconfirmedIncomeInSatoshi = 0L;
            var unconfirmedOutcomeInSatoshi = 0L;

            foreach (var output in outputs)
            {
                // unconfirmed income
                if (!output.IsConfirmed)
                    unconfirmedIncomeInSatoshi += output.Value;

                // unconfirmed outcome
                if ((output.SpentTxPoints?.Any() ?? false) && !output.IsSpentConfirmed)
                    unconfirmedOutcomeInSatoshi += output.Value;
            }

            return new BitcoinAddressInfo(
                Balance: balance,
                Received: received,
                Sent: received - balance,
                UnconfirmedIncome: unconfirmedIncomeInSatoshi.ToDecimal(_settings.Decimals),
                UnconfirmedOutcome: unconfirmedOutcomeInSatoshi.ToDecimal(_settings.Decimals),
                Outputs: outputs);
        }

        public static Network ResolveNetwork(string currency, string network)
        {
            return (currency, network) switch
            {
                ("BTC", "BTC") => Network.Main,
                ("BTC", "BTCTEST") => Network.TestNet,
                ("LTC", "LTC") => NBitcoin.Altcoins.Litecoin.Instance.Mainnet,
                ("LTC", "LTCTEST") => NBitcoin.Altcoins.Litecoin.Instance.Testnet,
                ("DOGE", "DOGE") => NBitcoin.Altcoins.Dogecoin.Instance.Mainnet,
                ("DOGE", "DOGETEST") => NBitcoin.Altcoins.Dogecoin.Instance.Testnet,
                ("DASH", "DASH") => NBitcoin.Altcoins.Dash.Instance.Mainnet,
                ("DASH", "DASHTEST") => NBitcoin.Altcoins.Dash.Instance.Testnet,
                _ => throw new NotSupportedException($"Currency {currency} with network {network} not supporeted.")
            };
        }

        public static Script ParseScript(string scriptOperands)
        {
            var ops = new List<Op>();

            foreach (var operand in scriptOperands.Split(' '))
            {
                var opBytes = Hex.FromString(operand.Length % 2 == 0
                    ? operand
                    : $"0{operand}");

                if (opBytes.Length == 1)
                {
                    var opcodeType = opBytes[0] == 1
                        ? OpcodeType.OP_1
                        : (OpcodeType)opBytes[0];

                    ops.Add(opcodeType);
                }
                else
                {
                    ops.Add(Op.GetPushOp(opBytes));
                }
            }

            return new Script(ops);
        }
    }
}