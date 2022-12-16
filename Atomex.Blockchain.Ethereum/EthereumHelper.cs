using System.Numerics;

namespace Atomex.Blockchain.Ethereum
{
    public static class EthereumHelper
    {
        public const long WeiInEth = 1000000000000000000;
        public const long WeiInGwei = 1000000000;
        public const string Eth = "ETH";

        public static decimal WeiToEth(BigInteger wei) =>
            (decimal)wei / WeiInEth;

        public static BigInteger EthToWei(decimal eth) =>
            new(eth * WeiInEth);

        public static BigInteger GweiToWei(decimal gwei) =>
            new(gwei * WeiInGwei);
    }
}