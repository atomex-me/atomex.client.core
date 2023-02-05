using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Atomex.Blockchain.Ethereum.EtherScan
{
    internal class Response<T>
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; }
        [JsonPropertyName("result")]
        public T Result { get; set; }
    }

    internal class RpcResponse<T>
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; }
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("result")]
        public T Result { get; set; }
    }

    public class RpcBlock
    {
        [JsonPropertyName("baseFeePerGas")]
        public string BaseFeePerGas { get; set; }
        [JsonPropertyName("difficulty")]
        public string Difficulty { get; set; }
        [JsonPropertyName("gasLimit")]
        public string GasLimit { get; set; }
        [JsonPropertyName("gasUsed")]
        public string GasUsed { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("miner")]
        public string Miner { get; set; }
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
        [JsonPropertyName("number")]
        public string Number { get; set; }
        [JsonPropertyName("size")]
        public string Size { get; set; }
        [JsonPropertyName("timestamp")]
        public string TimeStamp { get; set; }
        [JsonPropertyName("totalDifficulty")]
        public string TotalDifficulty { get; set; }
        [JsonPropertyName("transactions")]
        public List<string> Transactions { get; set; }
    }

    public class RpcTransactionReceipt
    {
        [JsonPropertyName("blockHash")]
        public string BlockHash { get; set; }
        [JsonPropertyName("blockNumber")]
        public string BlockNumber { get; set; }
        [JsonPropertyName("contractAddress")]
        public string ContractAddress { get; set; }
        [JsonPropertyName("cumulativeGasUsed")]
        public string CumulativeGasUsed { get; set; }
        [JsonPropertyName("effectiveGasPrice")]
        public string EffectiveGasPrice { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("gasUsed")]
        public string GasUsed { get; set; }
        [JsonPropertyName("logs")]
        public List<RpcLog> Logs { get; set; }
        [JsonPropertyName("logsBloom")]
        public string LogsBloom { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("transactionHash")]
        public string TransactionHash { get; set; }
        [JsonPropertyName("transactionIndex")]
        public string TransactionIndex { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    public class RpcTransaction
    {
        [JsonPropertyName("blockHash")]
        public string BlockHash { get; set; }
        [JsonPropertyName("blockNumber")]
        public string BlockNumber { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("gas")]
        public string Gas { get; set; }
        [JsonPropertyName("gasPrice")]
        public string GasPrice { get; set; }
        [JsonPropertyName("maxFeePerGas")]
        public string MaxFeePerGas { get; set; }
        [JsonPropertyName("maxPriorityFeePerGas")]
        public string MaxPriorityFeePerGas { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("input")]
        public string Input { get; set; }
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("transactionIndex")]
        public string TransactionIndex { get; set; }
        [JsonPropertyName("value")]
        public string Value { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("chainId")]
        public string ChainId { get; set; }
        [JsonPropertyName("v")]
        public string V { get; set; }
        [JsonPropertyName("r")]
        public string R { get; set; }
        [JsonPropertyName("s")]
        public string S { get; set; }
    }

    public class RpcLog
    {
        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("topics")]
        public List<string> Topics { get; set; }
        [JsonPropertyName("data")]
        public string Data { get; set; }
        [JsonPropertyName("blockNumber")]
        public string BlockNumber { get; set; }
        [JsonPropertyName("transactionHash")]
        public string TransactionHash { get; set; }
        [JsonPropertyName("transactionIndex")]
        public string TransactionIndex { get; set; }
        [JsonPropertyName("blockHash")]
        public string BlockHash { get; set; }
        [JsonPropertyName("logIndex")]
        public string LogIndex { get; set; }
        [JsonPropertyName("removed")]
        public bool Removed { get; set; }
    }

    public class TransactionDto
    {
        [JsonPropertyName("blockNumber")]
        public string BlockNumber { get; set; }
        [JsonPropertyName("timeStamp")]
        public string TimeStamp { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
        [JsonPropertyName("blockHash")]
        public string BlockHash { get; set; }
        [JsonPropertyName("transactionIndex")]
        public string TransactionIndex { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("value")]
        public string Value { get; set; }
        [JsonPropertyName("gas")]
        public string Gas { get; set; }
        [JsonPropertyName("gasPrice")]
        public string GasPrice { get; set; }
        [JsonPropertyName("isError")]
        public string IsError { get; set; }
        [JsonPropertyName("txreceipt_status")]
        public string TxReceiptStatus { get; set; }
        [JsonPropertyName("input")]
        public string Input { get; set; }
        [JsonPropertyName("contractAddress")]
        public string ContractAddress { get; set; }
        [JsonPropertyName("cumulativeGasUsed")]
        public string CumulativeGasUsed { get; set; }
        [JsonPropertyName("gasUsed")]
        public string GasUsed { get; set; }
        [JsonPropertyName("confirmations")]
        public string Confirmations { get; set; }
        [JsonPropertyName("methodId")]
        public string MethodId { get; set; }
        [JsonPropertyName("functionName")]
        public string FunctionName { get; set; }
    }

    public class InternalTransactionDto
    {
        [JsonPropertyName("blockNumber")]
        public string BlockNumber { get; set; }
        [JsonPropertyName("timeStamp")]
        public string TimeStamp { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("value")]
        public string Value { get; set; }
        [JsonPropertyName("contractAddress")]
        public string ContractAddress { get; set; }
        [JsonPropertyName("input")]
        public string Input { get; set; }
        [JsonPropertyName("type")]
        public string Type { get; set; }
        [JsonPropertyName("gas")]
        public string Gas { get; set; }
        [JsonPropertyName("gasUsed")]
        public string GasUsed { get; set; }
        [JsonPropertyName("traceId")]
        public string TraceId { get; set; }
        [JsonPropertyName("isError")]
        public string IsError { get; set; }
        [JsonPropertyName("errCode")]
        public string ErrCode { get; set; }
    }

    public class Erc20TransferDto
    {
        [JsonPropertyName("blockNumber")]
        public string BlockNumber { get; set; }
        [JsonPropertyName("timeStamp")]
        public string TimeStamp { get; set; }
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
        [JsonPropertyName("blockHash")]
        public string BlockHash { get; set; }
        [JsonPropertyName("from")]
        public string From { get; set; }
        [JsonPropertyName("contractAddress")]
        public string ContractAddress { get; set; }
        [JsonPropertyName("to")]
        public string To { get; set; }
        [JsonPropertyName("value")]
        public string Value { get; set; }
        [JsonPropertyName("tokenName")]
        public string TokenName { get; set; }
        [JsonPropertyName("tokenSymbol")]
        public string TokenSymbol { get; set; }
        [JsonPropertyName("tokenDecimal")]
        public string TokenDecimal { get; set; }
        [JsonPropertyName("transactionIndex")]
        public string TransactionIndex { get; set; }
        [JsonPropertyName("gas")]
        public string Gas { get; set; }
        [JsonPropertyName("gasPrice")]
        public string GasPrice { get; set; }
        [JsonPropertyName("gasUsed")]
        public string GasUsed { get; set; }
        [JsonPropertyName("cumulativeGasUsed")]
        public string CumulativeGasUsed { get; set; }
        [JsonPropertyName("input")]
        public string Input { get; set; }
        [JsonPropertyName("confirmations")]
        public string Confirmations { get; set; }
    }

    public class Erc721TransferDto : Erc20TransferDto
    {
        [JsonPropertyName("tokenID")]
        public string TokenId { get; set; }
    }

    public class ContractEvent
    {
        [JsonPropertyName("address")]
        public string Address { get; set; }
        [JsonPropertyName("topics")]
        public List<string> Topics { get; set; }
        [JsonPropertyName("data")]
        public string HexData { get; set; }
        [JsonPropertyName("blockNumber")]
        public string HexBlockNumber { get; set; }
        [JsonPropertyName("timeStamp")]
        public string HexTimeStamp { get; set; }
        [JsonPropertyName("gasPrice")]
        public string HexGasPrice { get; set; }
        [JsonPropertyName("gasUsed")]
        public string HexGasUsed { get; set; }
        [JsonPropertyName("logIndex")]
        public string HexLogIndex { get; set; }
        [JsonPropertyName("transactionHash")]
        public string HexTransactionHash { get; set; }
        [JsonPropertyName("transactionIndex")]
        public string HexTransactionIndex { get; set; }

        public string EventSignatureHash()
        {
            if (Topics != null && Topics.Count > 0)
                return Topics[0];

            throw new Exception("Contract event does not contain event signature hash");
        }
    }

    public class GasPrice
    {
        [JsonPropertyName("SafeGasPrice")]
        public long Safe { get; set; }
        [JsonPropertyName("ProposeGasPrice")]
        public long Propose { get; set; }
        [JsonPropertyName("FastGasPrice")]
        public long Fast { get; set; }
    }

    public enum ClosestBlock
    {
        Before,
        After
    }

    public enum TopicOperation
    {
        And,
        Or
    }

    public class TransactionsResult<T>
    {
        public List<T> Transactions { get; set; }
        public Dictionary<string, T> Index { get; set; }
    }
}