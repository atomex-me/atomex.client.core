namespace Atomex.Cryptography.BouncyCastle
{
    public static class Interleave
    {
        public static uint Shuffle2(uint x)
        {
            // "shuffle" (twice) low half to even bits and high half to odd bits
            uint t;
            t = (x ^ (x >> 7)) & 0x00AA00AAU; x ^= (t ^ (t << 7));
            t = (x ^ (x >> 14)) & 0x0000CCCCU; x ^= (t ^ (t << 14));
            t = (x ^ (x >> 4)) & 0x00F000F0U; x ^= (t ^ (t << 4));
            t = (x ^ (x >> 8)) & 0x0000FF00U; x ^= (t ^ (t << 8));
            return x;
        }
    }
}