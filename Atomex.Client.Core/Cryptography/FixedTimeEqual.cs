using System.Runtime.CompilerServices;

namespace Atomex.Cryptography
{
    public static class FixedTimeEqual
    {
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static unsafe bool Equals(byte* left, byte* right, int length)
        {
            unchecked
            {
                int accum = 0;

                for (int i = 0; i < length; i++)
                    accum |= left[i] - right[i];

                return accum == 0;
            }
        }
    }
}