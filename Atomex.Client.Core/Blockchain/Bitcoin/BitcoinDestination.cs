using NBitcoin;

using Atomex.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public class BitcoinDestination
    {
        public Script Script { get; set; }
        public decimal AmountInSatoshi { get; set; }

        public int Size()
        {
            var scriptBytes = Script.ToBytes();

            return 8                               // nValue
                + scriptBytes.Length.CompactSize() // scriptPubKey length
                + scriptBytes.Length;              // scriptPubKey
        }
    }
}