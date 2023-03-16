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
        public static IEnumerable<byte[]> ExtractAllPushData(this BitcoinTxInput i)
        {
            var result = new List<byte[]>();

            if (!string.IsNullOrEmpty(i.ScriptSig))
                result.AddRange(BitcoinSwapTemplate.ExtractAllPushData(Script.FromHex(i.ScriptSig)));

            if (!string.IsNullOrEmpty(i.WitScript))
                result.AddRange(BitcoinSwapTemplate.ExtractAllPushData(Script.FromHex(i.WitScript)));

            return result;
        }

        public static bool IsRedeem(this BitcoinTxInput i)
        {
            var scriptSig = Script.FromHex(i.ScriptSig);

            return BitcoinSwapTemplate.IsSwapRedeem(scriptSig);
        }

        public static bool IsRefund(this BitcoinTxInput i)
        {
            var scriptSig = Script.FromHex(i.ScriptSig);

            return BitcoinSwapTemplate.IsSwapRefund(scriptSig);
        }
    }
}