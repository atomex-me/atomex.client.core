using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using TransactionType = Atomex.Blockchain.Abstract.TransactionType;

namespace Atomex.Blockchain.Ethereum
{
    //public class EthereumInternalTransaction
    //{
    //    public long BlockHeight { get; set; }
    //    public DateTimeOffset? BlockTime { get; set; }
    //    public string Hash { get; set; }
    //    public string From { get; set; }
    //    public string To { get; set; }
    //    public BigInteger Value { get; set; }
    //    public BigInteger GasLimit { get; set; }
    //    public string Data { get; set; }
    //    public string Type { get; set; }
    //    public bool IsError { get; set; }
    //    public string ErrorDescription { get; set; }
    //}

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

        public string From { get; set; }
        public string To { get; set; }
        public string Input { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string RlpEncodedTx { get; set; }
        public bool ReceiptStatus { get; set; }
        public bool IsInternal { get; set; }
        public int InternalIndex { get; set; }
        public List<EthereumTransaction> InternalTxs { get; set; }

        public EthereumTransaction()
        {
        }

        public EthereumTransaction(string currency, TransactionInput txInput)
        {
            Currency     = currency;
            Status       = TransactionStatus.Pending;
            Type         = TransactionType.Unknown;
            CreationTime = DateTimeOffset.UtcNow;

            From         = txInput.From.ToLowerInvariant();
            To           = txInput.To.ToLowerInvariant();
            Input        = txInput.Data;
            Amount       = txInput.Value;
            Nonce        = txInput.Nonce;
            GasPrice     = txInput.GasPrice;
            GasLimit     = txInput.Gas;
        }

        public bool Verify() =>
            TransactionVerificationAndRecovery.VerifyTransaction(RlpEncodedTx);

        public byte[] ToBytes() => Encoding.UTF8.GetBytes(RlpEncodedTx);

        public byte[] GetRawHash(int chainId) =>
            new LegacyTransactionChainId(
                to: To,
                amount: Amount,
                nonce: Nonce,
                gasPrice: GasPrice,
                gasLimit: GasLimit,
                data: Input,
                chainId: chainId).RawHash;

        public string GetRlpEncoded(int chainId, byte[] signature)
        {
            var tx = new LegacyTransactionChainId(
                to: To,
                amount: Amount,
                nonce: Nonce,
                gasPrice: GasPrice,
                gasLimit: GasLimit,
                data: Input,
                chainId: chainId);

            tx.SetSignature(new EthECDSASignature(signature));

            return tx
                .GetRLPEncoded()
                .ToHexString();
        }
    }
}