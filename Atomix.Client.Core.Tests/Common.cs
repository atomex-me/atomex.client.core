using System.Text;
using Atomix.Cryptography;
using Atomix.Swaps;
using NBitcoin;

namespace Atomix.Client.Core.Tests
{
    public class Common
    {
        public static Key Alice { get; } = new Key();
        public static Key Bob { get; } = new Key();
        public static byte[] Secret { get; } = Encoding.UTF8.GetBytes("atomix");
        public static byte[] SecretHash { get; } = CurrencySwap.CreateSwapSecretHash(Secret);

        public static string AliceAddress(BitcoinBasedCurrency currency)
        {
            return Alice.PubKey.GetAddress(currency.Network).ToString();
        }

        public static string BobAddress(BitcoinBasedCurrency currency)
        {
            return Bob.PubKey.GetAddress(currency.Network).ToString();
        }

        public static string AliceSegwitAddress(BitcoinBasedCurrency currency)
        {
            return Alice.PubKey.GetSegwitAddress(currency.Network).ToString();
        }

        public static string BobSegwitAddress(BitcoinBasedCurrency currency)
        {
            return Bob.PubKey.GetSegwitAddress(currency.Network).ToString();
        }
    }
}