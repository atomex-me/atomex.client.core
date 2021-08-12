namespace Atomex.Blockchain.Tezos.Internal
{
    public static class Prefix
    {
        public static readonly byte[] Tz1 = { 6, 161, 159 };
        public static readonly byte[] Tz2 = { 6, 161, 161 };
        public static readonly byte[] Tz3 = { 6, 161, 164 };
        public static readonly byte[] KT = { 2, 90, 121 };
        public static readonly byte[] Edpk = { 13, 15, 37, 217 };
        public static readonly byte[] Edsk = { 13, 15, 58, 7 };
        public static readonly byte[] EdskSecretKey = { 43, 246, 78, 7 }; // ed25519 64 bytes secret key
        public static readonly byte[] Edsig = { 9, 245, 205, 134, 18 };
        public static readonly byte[] b = { 1, 52 };
    }
}