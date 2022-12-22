using System.Numerics;

using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumTransactionRequest
    {
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasPrice { get; set; }
        public BigInteger GasLimit { get; set; }
        public string Data { get; set; }
        public BigInteger ChainId { get; set; }
        public byte[] Signature { get; set; }

        public EthereumTransactionRequest()
        {
        }

        public EthereumTransactionRequest(TransactionInput txInput, BigInteger chainId)
        {
            From     = txInput.From.ToLowerInvariant();
            To       = txInput.To.ToLowerInvariant();
            Amount   = txInput.Value;
            Nonce    = txInput.Nonce;
            GasPrice = txInput.GasPrice;
            GasLimit = txInput.Gas;
            Data     = txInput.Data;
            ChainId  = chainId;
        }

        public byte[] GetRawHash() =>
            new LegacyTransactionChainId(
                to: To,
                amount: Amount,
                nonce: Nonce,
                gasPrice: GasPrice,
                gasLimit: GasLimit,
                data: Data,
                chainId: ChainId)
            .RawHash;

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