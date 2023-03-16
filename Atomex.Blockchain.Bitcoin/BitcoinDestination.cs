using System.Numerics;

using NBitcoin;

using Atomex.Blockchain.Bitcoin.Common;

namespace Atomex.Blockchain.Bitcoin
{
    public record BitcoinDestination
    {
        public Script Script { get; init; }
        public BigInteger AmountInSatoshi { get; init; }

        public int Size()
        {
            var scriptBytes = Script.ToBytes();

            return 8                               // nValue
                + scriptBytes.Length.CompactSize() // scriptPubKey length
                + scriptBytes.Length;              // scriptPubKey
        }
    }
}