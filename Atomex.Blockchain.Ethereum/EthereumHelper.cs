using System.Numerics;

using Atomex.Common;

namespace Atomex.Blockchain.Ethereum
{
    public static class EthereumHelper
    {
        public const int WeiDecimals = 18;
        public const int GweiDecimals = 9;
        public const long WeiInEth = 1000000000000000000;
        public const long WeiInGwei = 1000000000;
        public const long GweiInEth = 1000000000;
        public const string Eth = "ETH";
        public const byte Eip1559TransactionType = 0x02;

        public static decimal WeiToEth(this BigInteger wei)
        {
            try
            {
                return (decimal)wei / WeiInEth;
            }
            catch
            {
                // overflow
            }

            return wei.ToDecimal(WeiDecimals);
        }

        public static decimal WeiToGwei(this BigInteger wei)
        {
            try
            {
                return (decimal)wei / WeiInGwei;
            }
            catch
            {
                // overflow
            }

            return wei.ToDecimal(GweiDecimals);
        }   

        public static BigInteger EthToWei(this decimal eth) =>
            eth.Multiply(WeiInEth);

        public static BigInteger GweiToWei(this decimal gwei) =>
            gwei.Multiply(WeiInGwei);
    }
}