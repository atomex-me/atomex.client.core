using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NBitcoin;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Serilog;

using Atomex.Blockchain.Abstract;
using Atomex.Blockchain.BitcoinBased;
using Atomex.Common;

namespace Atomex.Blockchain.SoChain
{
    public class SoChainApi : BitcoinBasedBlockchainApi
    {
        internal class SendTx
        {
            [JsonProperty(PropertyName = "tx_hex")]
            public string TxHex { get; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; }

            public SendTx(string txHex, string network)
            {
                TxHex   = txHex;
                Network = network;
            }
        }

        internal class SendTxId
        {
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
        }

        internal class Output
        {
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }
            [JsonProperty(PropertyName = "script")]
            public string Script { get; set; }
        }

        internal class OutputRef
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
        }

        internal class Input
        {
            [JsonProperty(PropertyName = "from_output")]
            public OutputRef FromOutput { get; set; }
            [JsonProperty(PropertyName = "input_no")]
            public uint InputNo { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }
            [JsonProperty(PropertyName = "script")]
            public string Script { get; set; }
            [JsonProperty(PropertyName = "witness")]
            public List<string> Witness { get; set; }
        }

        internal class InputRef
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "input_no")]
            public uint InputNo { get; set; }
        }

        internal class Tx
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "blockhash")]
            public string BlockHash { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public List<Input> Inputs { get; set; }
            [JsonProperty(PropertyName = "outputs")]
            public List<Output> Outputs { get; set; }
            [JsonProperty(PropertyName = "tx_hex")]
            public string TxHex { get; set; }
            [JsonProperty(PropertyName = "block_no")]
            public int? BlockNo { get; set; }
            [JsonProperty(PropertyName = "sent_value")]
            public string SentValue { get; set; }
            [JsonProperty(PropertyName = "fee")]
            public string Fee { get; set; }
            //[JsonProperty(PropertyName = "size")]
            //public int Size { get; set; }
            //[JsonProperty(PropertyName = "version")]
            //public int Version { get; set; }
            //[JsonProperty(PropertyName = "locktime")]
            //public int LockTime { get; set; }
        }

        internal class TxInputs
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public List<Input> Inputs { get; set; }
        }

        internal class TxSingleInput
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public Input Inputs { get; set; }
        }

        internal class TxOutput
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
            [JsonProperty(PropertyName = "input_no")]
            public int InputNo { get; set; }
            [JsonProperty(PropertyName = "script_asm")]
            public string ScriptAsm { get; set; }
            [JsonProperty(PropertyName = "script_hex")]
            public string ScriptHex { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
        }

        internal class AddressOutputs
        {
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "txs")]
            public List<TxOutput> Txs { get; set; }
        }

        internal class InputDisplayData
        {
            [JsonProperty(PropertyName = "input_no")]
            public uint InputNo { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "received_from")]
            public OutputRef ReceivedFrom { get; set; }
        }

        internal class Incoming
        {
            [JsonProperty(PropertyName = "output_no")]
            public uint OutputNo { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "spent")]
            public InputRef Spent { get; set; }
            [JsonProperty(PropertyName = "inputs")]
            public List<InputDisplayData> Inputs { get; set; }
            [JsonProperty(PropertyName = "req_sigs")]
            public int? ReqSigs { get; set; }
            [JsonProperty(PropertyName = "script_asm")]
            public string ScriptAsm { get; set; }
            [JsonProperty(PropertyName = "script_hex")]
            public string ScriptHex { get; set; }
        }

        internal class OutputDisplayData
        {
            [JsonProperty(PropertyName = "output_no")]
            public int OutputNo { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "spent")]
            public InputRef Spent { get; set; }
        }

        internal class Outgoing
        {
            [JsonProperty(PropertyName = "value")]
            public string Value { get; set; }
            [JsonProperty(PropertyName = "outputs")]
            public List<OutputDisplayData> Outputs { get; set; }
        }

        internal class TxDisplayData
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "block_no")]
            public int? BlockNo { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public long Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
            [JsonProperty(PropertyName = "incoming")]
            public Incoming Incoming { get; set; }
            [JsonProperty(PropertyName = "outgoing")]
            public Outgoing Outgoing { get; set; }
        }

        internal class AddressDisplayData
        {
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "address")]
            public string Address { get; set; }
            [JsonProperty(PropertyName = "balance")]
            public string Balance { get; set; }
            [JsonProperty(PropertyName = "received_value")]
            public string ReceivedValue { get; set; }
            [JsonProperty(PropertyName = "pending_value")]
            public string PendingValue { get; set; }
            [JsonProperty(PropertyName = "total_txs")]
            public int TotalTxs { get; set; }
            [JsonProperty(PropertyName = "txs")]
            public List<TxDisplayData> Txs { get; set; }
        }

        internal class TxConfirmationInfo
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
            [JsonProperty(PropertyName = "is_confirmed")]
            public bool IsConfirmed { get; set; }
        }

        internal class TxOutputSpentInfo
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; set; }
            [JsonProperty(PropertyName = "output_no")]
            public uint OutputNo { get; set; }
            [JsonProperty(PropertyName = "is_spent")]
            public bool IsSpent { get; set; }
            [JsonProperty(PropertyName = "spent")]
            public InputRef Spent { get; set; }
        }

        internal class ConfidenceInfo
        {
            [JsonProperty(PropertyName = "txid")]
            public string TxId { get; set; }
            [JsonProperty(PropertyName = "confidence")]
            public decimal Confidence { get; set; }
            [JsonProperty(PropertyName = "confirmations")]
            public int Confirmations { get; set; }
        }

        internal class Response<T>
        {
            [JsonProperty(PropertyName = "status")]
            public string Status { get; set; }
            [JsonProperty(PropertyName = "data")]
            public T Data { get; set; }
        }

        private static Dictionary<string, string> Acronyms { get; } = new Dictionary<string, string>
        {
            { "Main", "BTC" },
            { "TestNet", "BTCTEST" },
            { "ltc-main", "LTC" },
            { "ltc-test", "LTCTEST" },
            { "dash-main", "DASH" },
            { "dash-test", "DASHTEST" },
            { "doge-main", "DOGE" },
            { "doge-test", "DOGETEST" },
            //{ "zec-main", "ZEC" },
            //{ "zec-test", "ZECTEST" }
        };

        private const int Satoshi = 100000000;
        private const int MinDelayBetweenRequestMs = 1000;

        private static readonly RequestLimitControl RequestLimitControl
            = new(MinDelayBetweenRequestMs);

        private readonly bool _useProxy = false;

        public BitcoinBasedConfig Currency { get; }
        public string NetworkAcronym { get; }
        public string BaseUrl { get; } = "https://sochain.com/";
        public string ProxyUrl { get; } = "https://test.atomex.me/";

        public SoChainApi(BitcoinBasedConfig currency, string baseUri)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));

            var networkAcronym = AcronymByNetwork(currency.Network);

            if (!Acronyms.ContainsValue(networkAcronym))
                throw new NotSupportedException($"Network {networkAcronym} not supported by chain.so api!");

            NetworkAcronym = networkAcronym;
            BaseUrl = baseUri;
        }

        public SoChainApi(BitcoinBasedConfig currency, IConfiguration configuration)
        {
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));

            var networkAcronym = AcronymByNetwork(currency.Network);

            if (!Acronyms.ContainsValue(networkAcronym))
                throw new NotSupportedException($"Network {networkAcronym} not supported by chain.so api!");

            NetworkAcronym = networkAcronym;
            BaseUrl = configuration["BlockchainApiBaseUri"];

            bool.TryParse(configuration["UseProxy"], out _useProxy);
        }

        public override async Task<Result<decimal>> GetBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var utxoResult = await GetUnspentOutputsAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (utxoResult.HasError)
                return utxoResult.Error;

            if (utxoResult.Value == null)
                return 0;

            var balanceInSatoshi = utxoResult.Value.Sum(o => o.Value);

            return Currency.SatoshiToCoin(balanceInSatoshi);
        }

        public override async Task<Result<ITxPoint>> GetInputAsync(
            string txId,
            uint inputNo,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/get_tx_inputs/{NetworkAcronym}/{txId}/{inputNo}";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<ITxPoint>(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var input = JsonConvert.DeserializeObject<Response<TxSingleInput>>(content).Data.Inputs;

                        var witScript = WitScript.Empty;

                        if (input.Witness != null)
                            witScript = input.Witness.Aggregate(witScript, (current, witness) => current + new WitScript(witness));

                        var script = input.Script != null
                            ? ParseScript(input.Script)
                            : Script.Empty;

                        var scriptHex = script.ToHex();

                        return new BitcoinBasedTxPoint(new IndexedTxIn
                        {
                            TxIn = new TxIn(new OutPoint(new uint256(input.FromOutput.TxId), input.FromOutput.OutputNo), script), // Script.FromHex(input.Script.Replace(" ", string.Empty))),
                            Index = input.InputNo,
                            WitScript = witScript,
                        });
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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

        public override async Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetUnspentOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            var addParams = afterTxId != null ? $"{address}/{afterTxId}" : $"{address}";
            var requestUri = $"api/v2/get_tx_unspent/{NetworkAcronym}/{addParams}";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var outputs = JsonConvert.DeserializeObject<Response<AddressOutputs>>(content);

                        var result = outputs.Data.Txs
                            .Select(u =>
                                new BitcoinBasedTxOutput(
                                    coin: new Coin(
                                        fromTxHash: new uint256(u.TxId),
                                        fromOutputIndex: (uint)u.OutputNo,
                                        amount: new Money(decimal.Parse(u.Value, CultureInfo.InvariantCulture),
                                            MoneyUnit.BTC),
                                        scriptPubKey: Script.FromHex(u.ScriptHex)),
                                    confirmations: u.Confirmations,
                                    spentTxPoint: null,
                                    spentTxConfirmations: 0));

                        return new Result<IEnumerable<BitcoinBasedTxOutput>>(result);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<IEnumerable<BitcoinBasedTxOutput>>> GetOutputsAsync(
            string address,
            string afterTxId = null,
            CancellationToken cancellationToken = default)
        {
            var addressInfoResult = await GetAddressInfo(address, cancellationToken)
                .ConfigureAwait(false);

            if (addressInfoResult.HasError)
                return addressInfoResult.Error;

            return new Result<IEnumerable<BitcoinBasedTxOutput>>(addressInfoResult.Value.Outputs);
        }

        public override async Task<Result<IBlockchainTransaction>> GetTransactionAsync(
            string txId,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/tx/{NetworkAcronym}/{txId}";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<IBlockchainTransaction>(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var tx = JsonConvert.DeserializeObject<Response<Tx>>(content);

                        return new BitcoinBasedTransaction(
                            currency: Currency.Name,
                            tx: Transaction.Parse(tx.Data.TxHex, Currency.Network),
                            blockInfo: new BlockInfo
                            {
                                Confirmations = tx.Data.Confirmations,
                                BlockHash     = tx.Data.BlockHash,
                                BlockHeight   = tx.Data.BlockNo.GetValueOrDefault(0),
                                BlockTime     = tx.Data.Time.ToUtcDateTime(),
                                FirstSeen     = tx.Data.Time.ToUtcDateTime()
                            },
                            fees: (long)(decimal.Parse(tx.Data.Fee, CultureInfo.InvariantCulture) * Satoshi)
                        );
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<ITxPoint>> IsTransactionOutputSpent(
            string txId,
            uint outputNo,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/is_tx_spent/{NetworkAcronym}/{txId}/{outputNo}";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<ITxPoint>(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var info = JsonConvert.DeserializeObject<Response<TxOutputSpentInfo>>(content);

                        return info.Data.IsSpent
                            ? new TxPoint(info.Data.Spent.InputNo, info.Data.Spent.TxId)
                            : null;
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<string>> BroadcastAsync(
            IBlockchainTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            var tx = (BitcoinBasedTransaction)transaction;

            var requestUri = $"api/v2/send_tx/{NetworkAcronym}";

            var txHex = tx.ToBytes().ToHexString();

            Log.Debug("TxHex: {@txHex}", txHex);

            tx.State = BlockchainTransactionState.Pending;

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            using var requestContent = new StringContent(
                content: JsonConvert.SerializeObject(new SendTx(txHex, NetworkAcronym)),
                encoding: Encoding.UTF8,
                mediaType: "application/json");

            return await HttpHelper.PostAsyncResult<string>(
                    baseUri: _useProxy
                        ? ProxyUrl
                        : BaseUrl,
                    requestUri: requestUri,
                    content: requestContent,
                    responseHandler: (response, content) => JsonConvert.DeserializeObject<Response<SendTxId>>(content).Data.TxId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        public override async Task<Result<BitcoinBasedAddressInfo>> GetAddressInfo(
            string address,
            CancellationToken cancellationToken = default)
        {
            var requestUri = $"api/v2/address/{NetworkAcronym}/{address}";

            await RequestLimitControl
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return await HttpHelper.GetAsyncResult<BitcoinBasedAddressInfo>(
                    baseUri: BaseUrl,
                    requestUri: requestUri,
                    responseHandler: (response, content) =>
                    {
                        var displayData = JsonConvert.DeserializeObject<Response<AddressDisplayData>>(content);

                        var balance = decimal.Parse(displayData.Data.Balance, CultureInfo.InvariantCulture);
                        var received = decimal.Parse(displayData.Data.ReceivedValue, CultureInfo.InvariantCulture);
                        var unconfirmedIncomeInSatoshi = 0L;
                        var unconfirmedOutcomeInSatoshi = 0L;

                        var outputs = new List<BitcoinBasedTxOutput>();
                        var outgoingTxConfirmations = new Dictionary<string, long>();
                        var unresolvedSpentTxConfirmations = new Dictionary<(string, uint), BitcoinBasedTxOutput>();

                        foreach (var tx in displayData.Data.Txs)
                        {
                            if (tx.Outgoing != null)
                            {
                                outgoingTxConfirmations.Add(tx.TxId, tx.Confirmations);
                            }

                            if (tx.Incoming != null)
                            {
                                var amount = decimal.Parse(tx.Incoming.Value, CultureInfo.InvariantCulture);

                                var script = Script.FromHex(tx.Incoming.ScriptHex);

                                var spentTxPoint = tx.Incoming.Spent != null
                                    ? new TxPoint(tx.Incoming.Spent.InputNo, tx.Incoming.Spent.TxId)
                                    : null;

                                var spentTxConfirmations = 0L;
                                var spentTxResolved = true;

                                if (spentTxPoint != null && !outgoingTxConfirmations.TryGetValue(spentTxPoint.Hash, out spentTxConfirmations))
                                {
                                    spentTxConfirmations = 0;
                                    spentTxResolved = false;
                                }

                                var output = new BitcoinBasedTxOutput(
                                    coin: new Coin(
                                        fromTxHash: new uint256(tx.TxId),
                                        fromOutputIndex: tx.Incoming.OutputNo,
                                        amount: new Money(
                                            amount: amount,
                                            unit: MoneyUnit.BTC),
                                        scriptPubKey: script),
                                    confirmations: tx.Confirmations,
                                    spentTxPoint: spentTxPoint,
                                    spentTxConfirmations: spentTxConfirmations);

                                outputs.Add(output);

                                if (!spentTxResolved)
                                    unresolvedSpentTxConfirmations.Add((tx.TxId, tx.Incoming.OutputNo), output);
                            }
                        }

                        // try resolve unresolved spent tx
                        if (unresolvedSpentTxConfirmations.Any())
                        {
                            foreach (var tx in unresolvedSpentTxConfirmations)
                            {
                                if (!outgoingTxConfirmations.TryGetValue(tx.Value.SpentTxPoint.Hash, out var spentTxConfirmations))
                                {
                                    Log.Warning("[SoChainApi] Can't find confirmations info for spent tx {@hash}", tx.Value.SpentTxPoint.Hash);
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

                        return new BitcoinBasedAddressInfo(
                            Balance: balance,
                            Received: received,
                            Sent: received - balance,
                            UnconfirmedIncome: Currency.SatoshiToCoin(unconfirmedIncomeInSatoshi),
                            UnconfirmedOutcome: Currency.SatoshiToCoin(unconfirmedOutcomeInSatoshi),
                            Outputs: outputs);
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static string AcronymByNetwork(Network network)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));

            if (Acronyms.TryGetValue(network.Name, out var acronym))
                return acronym;

            throw new NotSupportedException($"Network {network.Name} not supported!");
        }
    }
}