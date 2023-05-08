using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Signer.Crypto;
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
        public BigInteger Value { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger MaxFeePerGas { get; set; }
        public BigInteger MaxPriorityFeePerGas { get; set; }
        public BigInteger GasLimit { get; set; }
        public string? Data { get; set; }
        public BigInteger ChainId { get; set; }
        public List<EthereumAccessList>? AccessList { get; set; }
        public byte[]? Signature { get; set; }
        public byte[]? SignatureV { get; set; }

        public EthereumTransactionRequest()
        {
        }

        public EthereumTransactionRequest(TransactionInput txInput, BigInteger chainId)
        {
            From                 = txInput.From.ToLowerInvariant();
            To                   = txInput.To.ToLowerInvariant();
            Value                = txInput.Value;
            Nonce                = txInput.Nonce;
            MaxFeePerGas         = txInput.MaxFeePerGas;
            MaxPriorityFeePerGas = txInput.MaxPriorityFeePerGas;
            GasLimit             = txInput.Gas;
            Data                 = txInput.Data;
            ChainId              = chainId;
            AccessList = txInput.AccessList
                ?.Select(a => new EthereumAccessList
                {
                    Address = a.Address,
                    StorageKeys = a.StorageKeys
                })
                .ToList();
        }

        public Transaction1559 GetTransaction()
        {
            var signature = Signature != null
                ? new EthECDSASignature(Signature)
                : null;

            if (signature != null)
                signature.V = SignatureV;

            return new Transaction1559(
                chainId: ChainId,
                nonce: Nonce,
                maxPriorityFeePerGas: MaxPriorityFeePerGas,
                maxFeePerGas: MaxFeePerGas,
                gasLimit: GasLimit,
                receiverAddress: To,
                amount: Value,
                data: Data,
                accessList: AccessList
                    ?.Select(a => new AccessListItem
                    {
                        Address = a.Address,
                        StorageKeys = a.StorageKeys
                            .Select(k => Hex.FromString(k))
                            .ToList()
                    })
                    .ToList(),
                signature: signature);
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

            return tx
                .GetRLPEncoded()
                .ToHexString();
        }

        public bool Verify() => TransactionVerificationAndRecovery
            .VerifyTransaction(GetRlpEncoded());

        public static int CalculateRecId(ECDSASignature signature, byte[] hash, byte[] uncompressedPublicKey)
        {
            var recId = -1;

            for (var i = 0; i < 4; i++)
            {
                var rec = ECKey.RecoverFromSignature(i, signature, hash, false);
                if (rec != null)
                {
                    var k = rec.GetPubKey(false);
                    if (k != null && k.SequenceEqual(uncompressedPublicKey))
                    {
                        recId = i;
                        break;
                    }
                }
            }
            if (recId == -1)
                throw new Exception("Could not construct a recoverable key. This should never happen.");

            return recId;
        }
    }
}