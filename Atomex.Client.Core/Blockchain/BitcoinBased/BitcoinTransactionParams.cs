using System.Collections.Generic;

namespace Atomex.Blockchain.BitcoinBased
{
    public class BitcoinTransactionParams
    {
        public IEnumerable<BitcoinInputToSign> InputsToSign { get; set; }
        public IEnumerable<BitcoinDestination> Destinations { get; set; }
        public decimal FeeInSatoshi { get; set; }
        public decimal FeeRate { get; set; }
        public string ChangeAddress { get; set; }
    }
}