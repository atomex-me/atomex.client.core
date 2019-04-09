namespace Atomix.Blockchain.Tezos.Internal
{
    internal static class Watermark
    {
        public static readonly byte[] Block = { 1 };
        public static readonly byte[] Endorsement = { 2 };
        public static readonly byte[] Generic = { 3 };
    }
}