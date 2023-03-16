using System.Collections.Generic;

using Newtonsoft.Json;

namespace Atomex.Blockchain.Bitcoin.SoChain
{
    public partial class SoChainApi : BitcoinBlockchainApi
    {
        internal class SendTx
        {
            [JsonProperty(PropertyName = "tx_hex")]
            public string TxHex { get; }
            [JsonProperty(PropertyName = "network")]
            public string Network { get; }

            public SendTx(string txHex, string network)
            {
                TxHex = txHex;
                Network = network;
            }
        }

        internal class SendTxId
        {
            [JsonProperty(PropertyName = "network")]
            public string? Network { get; set; }
            [JsonProperty(PropertyName = "txid")]
            public string? TxId { get; set; }
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
            public long Confirmations { get; set; }
            [JsonProperty(PropertyName = "time")]
            public int Time { get; set; }
            [JsonProperty(PropertyName = "tx_hex")]
            public string TxHex { get; set; }
            [JsonProperty(PropertyName = "block_no")]
            public int? BlockNo { get; set; }
            [JsonProperty(PropertyName = "fee")]
            public string Fee { get; set; }
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

        internal class Response<T>
        {
            [JsonProperty(PropertyName = "status")]
            public string Status { get; set; }
            [JsonProperty(PropertyName = "data")]
            public T Data { get; set; }
        }
    }
}