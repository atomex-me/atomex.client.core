using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using LiteDB;

using Atomex.Blockchain.Abstract;
using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumTransaction : IBlockchainTransaction
    {
        private const int DefaultConfirmations = 1;

        [BsonField("TxId")]
        public string Id { get; set; }
        [BsonId]
        public string UniqueId => $"{Id}:{Currency}";
        public string Currency { get; set; }
        public BlockInfo BlockInfo { get; set; }
        public BlockchainTransactionState State { get; set; }
        public BlockchainTransactionType Type { get; set; }
        public DateTime? CreationTime { get; set; }
        [BsonIgnore]
        public bool IsConfirmed => BlockInfo?.Confirmations >= DefaultConfirmations;

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
            Type         = BlockchainTransactionType.Unknown;
            State        = BlockchainTransactionState.Unknown;
            CreationTime = DateTime.UtcNow;

            From         = txInput.From.ToLowerInvariant();
            To           = txInput.To.ToLowerInvariant();
            Input        = txInput.Data;
            Amount       = txInput.Value;
            Nonce        = txInput.Nonce;
            GasPrice     = txInput.GasPrice;
            GasLimit     = txInput.Gas;
        }

        public EthereumTransaction Clone()
        {
            var resTx = new EthereumTransaction()
            {
                Currency      = Currency,
                Id            = Id,
                Type          = Type,
                State         = State,
                CreationTime  = CreationTime,

                From          = From,
                To            = To,
                Input         = Input,
                Amount        = Amount,
                Nonce         = Nonce,
                GasPrice      = GasPrice,
                GasLimit      = GasLimit,
                GasUsed       = GasUsed,
                ReceiptStatus = ReceiptStatus,
                IsInternal    = IsInternal,
                InternalIndex = InternalIndex,
                InternalTxs   = new List<EthereumTransaction>(),

                BlockInfo = (BlockInfo)(BlockInfo?.Clone() ?? null)
            };

            if (InternalTxs != null)
                foreach (var intTx in InternalTxs)
                    resTx.InternalTxs.Add(intTx.Clone());

            return resTx;
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