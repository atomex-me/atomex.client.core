using System;

using Atomex.Core;

namespace Atomex.Common
{
    public class BitcoinNetworkResolver
    {
        public static NBitcoin.Network ResolveNetwork(string currency, Network network)
        {
            return (currency, network) switch
            {
                ("BTC", Network.MainNet) => NBitcoin.Network.Main,
                ("BTC", Network.TestNet) => NBitcoin.Network.TestNet,
                ("LTC", Network.MainNet) => NBitcoin.Altcoins.Litecoin.Instance.Mainnet,
                ("LTC", Network.TestNet) => NBitcoin.Altcoins.Litecoin.Instance.Testnet,
                _ => throw new Exception($"Invalid currency {currency} or network {network}")
            };
        }
    }
}