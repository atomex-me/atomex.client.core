using System;
using System.Collections.Generic;
using Atomex.Blockchain.Abstract;
using NBitcoin;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinBasedTxPoint : ITxPoint
    {
        private readonly IndexedTxIn _input;

        public uint Index => _input.PrevOut.N;
        public string Hash => _input.PrevOut.Hash.ToString();

        public BitcoinBasedTxPoint(IndexedTxIn input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public IEnumerable<byte[]> ExtractAllPushData()
        {
            return BitcoinBasedSwapTemplate.ExtractAllPushData(_input.ScriptSig);
        }

        public bool IsRedeem()
        {
            return BitcoinBasedSwapTemplate.IsP2PkhSwapRedeem(_input.ScriptSig) ||
                BitcoinBasedSwapTemplate.IsP2PkhScriptSwapRedeem(_input.ScriptSig);
        }

        public bool IsRefund()
        {
            return BitcoinBasedSwapTemplate.IsP2PkhSwapRefund(_input.ScriptSig) || 
                BitcoinBasedSwapTemplate.IsP2PkhScriptSwapRefund(_input.ScriptSig);
        }
    }
}