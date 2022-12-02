using System;
using System.Collections.Generic;

using NBitcoin;

using Atomex.Blockchain.Abstract;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTxPoint : ITxPoint
    {
        private readonly IndexedTxIn _input;

        public uint Index => _input.PrevOut.N;
        public string Hash => _input.PrevOut.Hash.ToString();

        public BitcoinTxPoint(IndexedTxIn input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public IEnumerable<byte[]> ExtractAllPushData() =>
            BitcoinSwapTemplate.ExtractAllPushData(_input.ScriptSig);

        public bool IsRedeem() =>
            BitcoinSwapTemplate.IsP2PkhSwapRedeem(_input.ScriptSig) ||
            BitcoinSwapTemplate.IsP2PkhScriptSwapRedeem(_input.ScriptSig);

        public bool IsRefund() =>
            BitcoinSwapTemplate.IsP2PkhSwapRefund(_input.ScriptSig) || 
            BitcoinSwapTemplate.IsP2PkhScriptSwapRefund(_input.ScriptSig);
    }
}