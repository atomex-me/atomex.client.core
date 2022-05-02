using System;
using System.Collections.Generic;
using System.Numerics;

using Nethereum.Signer;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Transaction = Atomex.Blockchain.Abstract.Transaction;

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

    public class EthereumTransaction : Transaction
    {
        public override string TxId { get; set; }
        public override string Currency { get; set; }
        public override TransactionStatus Status { get; set; }
        public override DateTimeOffset? CreationTime { get; set; }
        public override DateTimeOffset? BlockTime { get; set; }
        public override long BlockHeight { get; set; }
        public override long Confirmations { get; set; }

        public string From { get; set; }
        public string To { get; set; }
        public BigInteger ChainId { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public BigInteger GasUsed { get; set; }
        public string Data { get; set; }
        public bool IsError { get; set; }
        public string ErrorDescription { get; set; }
        public List<EthereumInternalTransaction> InternalTransactions { get; set; }

        public byte[] Signature { get; set; }

        public byte[] GetRawHash() =>
            new LegacyTransactionChainId(
                to: To,
                amount: Amount,
                nonce: Nonce,
                gasPrice: GasPrice,
                gasLimit: GasLimit,
                data: Data,
                chainId: ChainId).RawHash;

        public string GetRlpEncoded()
        {
            var tx = new LegacyTransactionChainId(
                to: To,
                amount: Amount,
                nonce: Nonce,
                gasPrice: GasPrice,
                gasLimit: GasLimit,
                data: Data,
                chainId: ChainId);

            tx.SetSignature(new EthECDSASignature(Signature));

            return tx
                .GetRLPEncoded()
                .ToHexString();
        }

        public bool Verify() => TransactionVerificationAndRecovery
            .VerifyTransaction(GetRlpEncoded());
    }
}