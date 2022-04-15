using System;
using System.Runtime.InteropServices;

namespace Atomex.Common.Libsodium
{
    internal static partial class Interop
    {
        internal static partial class Libsodium
        {
            internal const int crypto_auth_hmacsha256_BYTES = 32;
            internal const int crypto_auth_hmacsha256_KEYBYTES = 32;

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_auth_hmacsha256_init(
                crypto_auth_hmacsha256_state* state,
                byte* key,
                UIntPtr keylen);

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_auth_hmacsha256_update(
                crypto_auth_hmacsha256_state* state,
                byte* @in,
                ulong inlen);

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_auth_hmacsha256_final(
                crypto_auth_hmacsha256_state* state,
                byte* @out);

            [StructLayout(LayoutKind.Explicit, Size = 208)]
            internal struct crypto_auth_hmacsha256_state
            {
            }
        }
    }
}