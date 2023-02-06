using System.Collections.Generic;
using System.Numerics;
using System.Linq;

using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Util;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public class EthereumAccessList
    {
        public string Address { get; set; }
        public List<string> StorageKeys { get; set; }
    }

    public class EthereumTransactionRequest
    {
        public string From { get; set; }
        public string To { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger MaxFeePerGas { get; set; }
        public BigInteger MaxPriorityFeePerGas { get; set; }
        public BigInteger GasLimit { get; set; }
        public string Data { get; set; }
        public BigInteger ChainId { get; set; }
        public List<EthereumAccessList> AccessList { get; set; }
        public byte[] Signature { get; set; }

        public EthereumTransactionRequest()
        {
        }

        public EthereumTransactionRequest(TransactionInput txInput, BigInteger chainId)
        {
            From                 = txInput.From.ToLowerInvariant();
            To                   = txInput.To.ToLowerInvariant();
            Amount               = txInput.Value;
            Nonce                = txInput.Nonce;
            MaxFeePerGas         = txInput.MaxFeePerGas;
            MaxPriorityFeePerGas = txInput.MaxPriorityFeePerGas;
            GasLimit             = txInput.Gas;
            Data                 = txInput.Data;
            ChainId              = chainId;
            AccessList = txInput.AccessList
                .Select(a => new EthereumAccessList
                {
                    Address = a.Address,
                    StorageKeys = a.StorageKeys
                })
                .ToList();
        }

        private Transaction1559 GetTransaction()
        {
            return new Transaction1559(
                chainId: ChainId,
                nonce: Nonce,
                maxPriorityFeePerGas: MaxPriorityFeePerGas,
                maxFeePerGas: MaxFeePerGas,
                gasLimit: GasLimit,
                receiverAddress: To,
                amount: Amount,
                data: Data,
                accessList: AccessList
                    .Select(a => new AccessListItem
                    {
                        Address = a.Address,
                        StorageKeys = a.StorageKeys
                            .Select(k => Hex.FromString(k))
                            .ToList()
                    })
                    .ToList());
        }

        public byte[] GetRawHash()
        {
            var rlp = GetTransaction()
                .GetRLPEncodedRaw();

            return new Sha3Keccack()
                .CalculateHash(rlp);
        }

        public string GetRlpEncoded()
        {
            var tx = GetTransaction();

            tx.SetSignature(new EthECDSASignature(Signature));

            return tx
                .GetRLPEncoded()
                .ToHexString();
        }

        public bool Verify() => TransactionVerificationAndRecovery
            .VerifyTransaction(GetRlpEncoded());
    }
}