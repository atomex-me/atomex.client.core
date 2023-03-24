using Atomex.Cryptography;

namespace Atomex.Blockchain.Tezos.Common
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

        public static bool CheckTz1Address(string address) =>
            CheckAddress(address, TezosPrefix.Tz1);

        public static bool CheckTz2Address(string address) =>
            CheckAddress(address, TezosPrefix.Tz2);

        public static bool CheckTz3Address(string address) =>
            CheckAddress(address, TezosPrefix.Tz3);

        public static bool CheckKtAddress(string address) =>
            CheckAddress(address, TezosPrefix.KT);
    }
}