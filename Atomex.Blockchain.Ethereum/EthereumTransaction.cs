using System;
using System.Collections.Generic;
using System.Numerics;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Ethereum
{
    public interface IEthereumTransaction
    {
        public long BlockHeight { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Value { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string Data { get; set; }
    }

    public class EthereumInternalTransaction : IEthereumTransaction
    {
        public long BlockHeight { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public string Hash { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Value { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string Data { get; set; }
        public string Type { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
    }

    public class EthereumTransaction : ITransaction, IEthereumTransaction
    {
        public string UniqueId => $"{Id}:{Currency}";
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionStatus Status { get; set; }
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;

        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Value { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger MaxFeePerGas { get; set; }
        public BigInteger MaxPriorityFeePerGas { get; set; }
        public BigInteger GasUsed { get; set; }
        public string Data { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
        public List<EthereumInternalTransaction>? InternalTransactions { get; set; }

        public EthereumTransaction()
        {          
        }

        public EthereumTransaction(EthereumTransactionRequest txRequest)
        {
            Currency             = EthereumHelper.Eth;
            Status               = TransactionStatus.Pending;
            CreationTime         = DateTimeOffset.UtcNow;
            From                 = txRequest.From.ToLowerInvariant();
            To                   = txRequest.To.ToLowerInvariant();
            Data                 = txRequest.Data;
            Value                = txRequest.Value;
            Nonce                = txRequest.Nonce;
            MaxFeePerGas         = txRequest.MaxFeePerGas;
            MaxPriorityFeePerGas = txRequest.MaxPriorityFeePerGas;
            GasLimit             = txRequest.GasLimit;
            //GasPrice = txRequest.GasPrice;
        }
    }
}