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
            new BigInteger(eth * WeiInEth);

        public static BigInteger GweiToWei(decimal gwei) =>
            new BigInteger(gwei * WeiInGwei);

        public static BigInteger TokensToBaseTokenUnits(decimal tokens, decimal decimalsMultiplier) =>
            new BigInteger(tokens * decimalsMultiplier);

        public static decimal BaseTokenUnitsToTokens(BigInteger tokenUnits, decimal decimalsMultiplier) =>
            (decimal)tokenUnits / decimalsMultiplier; // todo: use BigInteger or BigDecimal
    }
}