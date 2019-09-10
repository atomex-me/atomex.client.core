using System;
using System.Collections.Generic;
using Atomix.Blockchain.Abstract;
using Atomix.Core;
using NBitcoin;

namespace Atomix.Blockchain.BitcoinBased
{
    public interface IBitcoinBasedTransaction : IInOutTransaction
    {
        long TotalOut { get; }

        long GetFee(ITxOutput[] spentOutputs);
        byte[] GetSignatureHash(ITxOutput spentOutput);
        Script GetScriptSig(int inputNo);

        void Sign(byte[] privateKey, ITxOutput[] spentOutputs);
        void Sign(Key privateKey, ITxOutput spentOutput);
        void Sign(Key privateKey, ITxOutput[] spentOutputs);
        void NonStandardSign(byte[] sigScript, ITxOutput spentOutput);
        void NonStandardSign(Script sigScript, ITxOutput spentOutput);
        void NonStandardSign(byte[] sigScript, int inputNo);
        void NonStandardSign(Script sigScript, int inputNo);

        bool Check();
        bool Verify(ITxOutput spentOutput);
        bool Verify(ITxOutput spentOutput, out Error[] errors);
        bool Verify(IEnumerable<ITxOutput> spentOutputs);
        bool Verify(IEnumerable<ITxOutput> spentOutputs, out Error[] errors);

        int VirtualSize();
        IBitcoinBasedTransaction Clone();
        byte[] ToBytes();
    }
}