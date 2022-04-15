using System;
using System.Runtime.CompilerServices;

namespace Atomex.Common.Memory
{
    public static class Utils
    {
        public unsafe static void Memset<T>(IntPtr ptr, uint length, byte value)
        {
            Unsafe.InitBlock(ptr.ToPointer(), value, checked(length * (uint)Unsafe.SizeOf<T>()));
        }
    }
}