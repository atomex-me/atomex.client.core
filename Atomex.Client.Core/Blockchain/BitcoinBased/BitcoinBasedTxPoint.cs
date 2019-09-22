using System;
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

        public byte[] ExtractSecret()
        {
            if (BitcoinBasedSwapTemplate.IsP2PkhSwapRedeem(_input.ScriptSig))
                return BitcoinBasedSwapTemplate.ExtractSecretFromP2PkhSwapRedeem(_input.ScriptSig);
            if (BitcoinBasedSwapTemplate.IsP2PkhScriptSwapRedeem(_input.ScriptSig))
                return BitcoinBasedSwapTemplate.ExtractSecretFromP2PkhScriptSwapRedeem(_input.ScriptSig);

            // todo: segwit p2pkh swap redeem

            throw new NotSupportedException("Can't extract secret from scriptsig. Unknown script type.");
        }
        //public WalletAddress FromAddress(BitcoinBaseCurrency currency)
        //{
        //    //if (!IsStandard)
        //    //    return null;

        //   return new WalletAddress
        //    {
        //        Address = _input.ScriptSig.GetDestinationAddress(currency.Network).ToString(),
        //        Currency = currency
        //    };
        //}
    }
}