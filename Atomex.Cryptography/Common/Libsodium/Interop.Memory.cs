using System;
using System.Runtime.InteropServices;

namespace Atomex.Common.Libsodium
{
    internal static partial class Interop
    {
        internal static partial class Libsodium
        {
            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void sodium_free(IntPtr ptr);

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sodium_malloc(UIntPtr size);
        }
    }
}