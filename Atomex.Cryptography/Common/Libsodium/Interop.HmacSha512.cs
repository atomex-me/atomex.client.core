﻿using System;
using System.Runtime.InteropServices;

namespace Atomex.Common.Libsodium
{
    internal static partial class Interop
    {
        internal static partial class Libsodium
        {
            internal const int crypto_auth_hmacsha512_BYTES = 64;
            internal const int crypto_auth_hmacsha512_KEYBYTES = 32;

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_auth_hmacsha512_init(
                crypto_auth_hmacsha512_state* state,
                byte* key,
                UIntPtr keylen);

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_auth_hmacsha512_update(
                crypto_auth_hmacsha512_state* state,
                byte* @in,
                ulong inlen);

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_auth_hmacsha512_final(
                crypto_auth_hmacsha512_state* state,
                byte* @out);

            [StructLayout(LayoutKind.Explicit, Size = 416)]
            internal struct crypto_auth_hmacsha512_state
            {
            }
        }
    }
}