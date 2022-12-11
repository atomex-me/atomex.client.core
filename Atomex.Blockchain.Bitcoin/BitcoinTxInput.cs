using System.Collections.Generic;

using NBitcoin;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinTxInput
    {
        public uint Index { get; set; }
        public BitcoinTxPoint PreviousOutput { get; set; }
        public string ScriptSig { get; set; }
        public string WitScript { get; set; }
    }

    public static class BitcoinTxInputExtensions
    {
        public static IEnumerable<byte[]> ExtractAllPushData(this BitcoinTxInput i) =>
            BitcoinSwapTemplate.ExtractAllPushData(Script.FromHex(i.ScriptSig));

        public static bool IsRedeem(this BitcoinTxInput i)
        {
            var scriptSig = Script.FromHex(i.ScriptSig);

            return BitcoinSwapTemplate.IsP2PkhSwapRedeem(scriptSig) ||
                BitcoinSwapTemplate.IsP2PkhScriptSwapRedeem(scriptSig);
        }

        public static bool IsRefund(this BitcoinTxInput i)
        {
            var scriptSig = Script.FromHex(i.ScriptSig);

            return BitcoinSwapTemplate.IsP2PkhSwapRefund(scriptSig) ||
                BitcoinSwapTemplate.IsP2PkhScriptSwapRefund(scriptSig);
        }
    }
}