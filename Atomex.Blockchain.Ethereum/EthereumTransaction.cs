using System;
using System.Collections.Generic;
using System.Numerics;

using Atomex.Blockchain.Abstract;
using TransactionType = Atomex.Blockchain.Abstract.TransactionType;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumInternalTransaction
    {
        public long BlockHeight { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public string Hash { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Value { get; set; }
        public BigInteger GasLimit { get; set; }
        public string Data { get; set; }
        public string Type { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
    }

    public class EthereumTransaction : ITransaction
    {
        public string Id { get; set; }
        public string Currency { get; set; }
        public TransactionStatus Status { get; set; }
        public TransactionType Type { get; set; }
        public DateTimeOffset? CreationTime { get; set; }
        public DateTimeOffset? BlockTime { get; set; }
        public long BlockHeight { get; set; }
        public long Confirmations { get; set; }
        public bool IsConfirmed => Confirmations > 0;
        public bool IsTypeResolved => Type != TransactionType.Unknown;

        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string Data { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
        public List<EthereumInternalTransaction>? InternalTransactions { get; set; }

        public EthereumTransaction()
        {
            
        }

        public EthereumTransaction(EthereumTransactionRequest txRequest, TransactionType type = TransactionType.Output)
        {
            Currency     = EthereumHelper.Eth;
            Status       = TransactionStatus.Pending;
            Type         = type;
            CreationTime = DateTimeOffset.UtcNow;

            From     = txRequest.From.ToLowerInvariant();
            To       = txRequest.To.ToLowerInvariant();
            Data     = txRequest.Data;
            Amount   = txRequest.Amount;
            Nonce    = txRequest.Nonce;
            GasPrice = txRequest.GasPrice;
            GasLimit = txRequest.GasLimit;
        }
    }
}