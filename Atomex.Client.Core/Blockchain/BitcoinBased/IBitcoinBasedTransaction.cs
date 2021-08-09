using System.Collections.Generic;

using NBitcoin;

using Atomex.Blockchain.Abstract;
using Atomex.Common;
using Atomex.Core;

namespace Atomex.Blockchain.BitcoinBased
{
    public interface IBitcoinBasedTransaction : IInOutTransaction
    {
        long TotalOut { get; }

        long GetFee(ITxOutput[] spentOutputs);
        byte[] GetSignatureHash(ITxOutput spentOutput);
        byte[] GetSignatureHash(Script redeemScript, ITxOutput spentOutput);
        Script GetScriptSig(int inputNo);

        void Sign(SecureBytes privateKey, ITxOutput[] spentOutputs, BitcoinBasedConfig bitcoinBasedConfig);
        void Sign(Key privateKey, ITxOutput spentOutput, BitcoinBasedConfig bitcoinBasedConfig);
        void Sign(Key privateKey, ITxOutput[] spentOutputs, BitcoinBasedConfig bitcoinBasedConfig);
        void NonStandardSign(byte[] sigScript, ITxOutput spentOutput);
        void NonStandardSign(Script sigScript, ITxOutput spentOutput);
        void NonStandardSign(byte[] sigScript, int inputNo);
        void NonStandardSign(Script sigScript, int inputNo);

        bool Check();
        bool Verify(ITxOutput spentOutput, BitcoinBasedConfig bitcoinBasedConfig, bool checkScriptPubKey = true);
        bool Verify(ITxOutput spentOutput, out Error[] errors, BitcoinBasedConfig bitcoinBasedConfig, bool checkScriptPubKey = true);
        bool Verify(IEnumerable<ITxOutput> spentOutputs, BitcoinBasedConfig bitcoinBasedConfig, bool checkScriptPubKey = true);
        bool Verify(IEnumerable<ITxOutput> spentOutputs, out Error[] errors, BitcoinBasedConfig bitcoinBasedConfig, bool checkScriptPubKey = true);

        int VirtualSize();
        IBitcoinBasedTransaction Clone();
        byte[] ToBytes();
        long GetDust(long minOutputValue);
        void SetSequenceNumber(uint sequenceNumber);
        uint GetSequenceNumber(int inputIndex);
    }
}