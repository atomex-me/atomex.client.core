using System;

using NBitcoin;

namespace Atomex.Blockchain.Bitcoin.Common
{
    public static class BitcoinAddressExtensions
    {
        public static Script GetAddressScript(this string bitcoinAddress, Network expectedNetwork) =>
             BitcoinAddress.Create(bitcoinAddress, expectedNetwork).ScriptPubKey;

        public static byte[] GetAddressHash(this string bitcoinAddress, Network expectedNetwork)
        {
            return BitcoinAddress.Create(bitcoinAddress, expectedNetwork) switch
            {
                BitcoinPubKeyAddress a => a.Hash.ToBytes(),
                BitcoinWitPubKeyAddress a => a.Hash.ToBytes(),
                _ => throw new NotSupportedException($"Address {bitcoinAddress} not supporeted.")
            };
        }

        public static bool IsSegWitAddress(this string bitcoinAddress, Network expectedNetwork) =>
            BitcoinAddress.Create(bitcoinAddress, expectedNetwork) is BitcoinWitPubKeyAddress;
    }
}