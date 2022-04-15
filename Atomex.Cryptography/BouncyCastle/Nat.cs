namespace Atomex.Cryptography.BouncyCastle
{
    public static class Nat
    {
        public static uint CAdd(int len, int mask, uint[] x, uint[] y, uint[] z)
        {
            uint MASK = (uint)-(mask & 1);

            ulong c = 0;
            for (int i = 0; i < len; ++i)
            {
                c += (ulong)x[i] + (y[i] & MASK);
                z[i] = (uint)c;
                c >>= 32;
            }
            return (uint)c;
        }

        public static void CMov(int len, int mask, int[] x, int xOff, int[] z, int zOff)
        {
            mask = -(mask & 1);

            for (int i = 0; i < len; ++i)
            {
                int z_i = z[zOff + i], diff = z_i ^ x[xOff + i];
                z_i ^= (diff & mask);
                z[zOff + i] = z_i;
            }

            //int half = 0x55555555, rest = half << (-mask);

            //for (int i = 0; i < len; ++i)
            //{
            //    int z_i = z[zOff + i], diff = z_i ^ x[xOff + i];
            //    z_i ^= (diff & half);
            //    z_i ^= (diff & rest);
            //    z[zOff + i] = z_i;
            //}
        }

        public static uint ShiftDownBit(int len, uint[] z, uint c)
        {
            int i = len;
            while (--i >= 0)
            {
                uint next = z[i];
                z[i] = (next >> 1) | (c << 31);
                c = next;
            }
            return c << 31;
        }
    }
}