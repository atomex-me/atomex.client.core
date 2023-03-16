using Atomex.Blockchain.Ethereum;
using Atomex.Common;
using Atomex.Common.Memory;
using Atomex.Wallets.Ethereum;
using Nethereum.Hex.HexTypes;
using Nethereum.Signer;
using Nethereum.Signer.Crypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace Atomex.Client.Core.Tests.Wallets.Ethereum
{
    public class EthereumTransactionSignTests
    {
        [Fact]
        public void CanSign()
        {
            var seed = new byte[32];

            new Random(Seed: 0).NextBytes(seed);

            var key = new EthereumKey(new SecureBytes(seed));

            var securePublicKey = key.GetPublicKey();

            var publicKey = securePublicKey.ToUnsecuredBytes();

            var ethEcKey = new EthECKey(publicKey, false);

            var address = ethEcKey
                .GetPublicAddress()
                .ToLowerInvariant();

            var chainId = 1;

            var txRequest = new EthereumTransactionRequest(txInput: new Nethereum.RPC.Eth.DTOs.TransactionInput
            {
                From = address,
                To = address,
                ChainId = new HexBigInteger(chainId),
                Data = null,
                AccessList = null,
                Gas = new HexBigInteger(21000),
                //GasPrice = new HexBigInteger(20),
                MaxFeePerGas = new HexBigInteger(20),
                MaxPriorityFeePerGas = new HexBigInteger(1),
                Nonce = new HexBigInteger(0),
                Type = new HexBigInteger(EthereumHelper.Eip1559TransactionType),
                Value = new HexBigInteger(0)

            }, chainId);

            var rawHash = txRequest.GetRawHash();

            //var signature = key.Sign(rawHash);

            var privEthEcKey = new EthECKey(key.GetPrivateKey().ToUnsecuredBytes(), true);

            var signatureEcdsa = privEthEcKey
                .Sign(rawHash);
                //.SignAndCalculateYParityV(rawHash);
                //.SignAndCalculateV(rawHash);

            var signature = signatureEcdsa
                .ToDER();

            var rlpRaw = txRequest
                .GetTransaction()
                .GetRLPEncodedRaw()
                .ToHexString();

            var rlpEncoded = txRequest.GetRlpEncoded();

            txRequest.Signature = signature;
            txRequest.SignatureV = new byte[] { (byte)EthereumTransactionRequest.CalculateRecId(new ECDSASignature(signature), rawHash, publicKey) };

            var newRlpEncoded = txRequest.GetRlpEncoded();

            var tx = TransactionFactory.CreateTransaction(newRlpEncoded);

            var verifyResult = TransactionVerificationAndRecovery
                .VerifyTransaction(txRequest.GetTransaction());//txRequest.Verify();

            verifyResult = TransactionVerificationAndRecovery
                .VerifyTransaction(newRlpEncoded);//txRequest.Verify();
        }


    }
}