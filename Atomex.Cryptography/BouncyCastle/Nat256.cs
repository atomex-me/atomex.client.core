namespace Atomex.Cryptography.BouncyCastle
{
    public static class Nat256
    {
        public static bool Gte(uint[] x, uint[] y)
        {
            for (int i = 7; i >= 0; --i)
            {
                uint x_i = x[i], y_i = y[i];
                if (x_i < y_i)
                    return false;
                if (x_i > y_i)
                    return true;
            }
            return true;
        }

        public static uint MulAddTo(uint[] x, uint[] y, uint[] zz)
        {
            ulong y_0 = y[0];
            ulong y_1 = y[1];
            ulong y_2 = y[2];
            ulong y_3 = y[3];
            ulong y_4 = y[4];
            ulong y_5 = y[5];
            ulong y_6 = y[6];
            ulong y_7 = y[7];

            ulong zc = 0;
            for (int i = 0; i < 8; ++i)
            {
                ulong c = 0, x_i = x[i];
                c += x_i * y_0 + zz[i + 0];
                zz[i + 0] = (uint)c;
                c >>= 32;
                c += x_i * y_1 + zz[i + 1];
                zz[i + 1] = (uint)c;
                c >>= 32;
                c += x_i * y_2 + zz[i + 2];
                zz[i + 2] = (uint)c;
                c >>= 32;
                c += x_i * y_3 + zz[i + 3];
                zz[i + 3] = (uint)c;
                c >>= 32;
                c += x_i * y_4 + zz[i + 4];
                zz[i + 4] = (uint)c;
                c >>= 32;
                c += x_i * y_5 + zz[i + 5];
                zz[i + 5] = (uint)c;
                c >>= 32;
                c += x_i * y_6 + zz[i + 6];
                zz[i + 6] = (uint)c;
                c >>= 32;
                c += x_i * y_7 + zz[i + 7];
                zz[i + 7] = (uint)c;
                c >>= 32;
                c += zc + zz[i + 8];
                zz[i + 8] = (uint)c;
                zc = c >> 32;
            }
            return (uint)zc;
        }
    }
}