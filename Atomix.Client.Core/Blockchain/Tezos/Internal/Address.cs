using Atomix.Cryptography;

namespace Atomix.Blockchain.Tezos.Internal
{
    public static class Address
    {
        public static bool CheckAddress(string address, byte[] prefix)
        {
            try
            {
                Base58Check.Decode(address, prefix);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckTz1Address(string address)
        {
            return CheckAddress(address, Prefix.Tz1);
        }

        public static bool CheckTz2Address(string address)
        {
            return CheckAddress(address, Prefix.Tz2);
        }

        public static bool CheckTz3Address(string address)
        {
            return CheckAddress(address, Prefix.Tz3);
        }

        public static bool CheckKtAddress(string address)
        {
            return CheckAddress(address, Prefix.KT);
        }
    }
}