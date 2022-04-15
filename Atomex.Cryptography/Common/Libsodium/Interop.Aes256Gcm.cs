using System.Runtime.InteropServices;

namespace Atomex.Common.Libsodium
{
    internal static partial class Interop
    {
        internal static partial class Libsodium
        {
            internal const int crypto_aead_aes256gcm_ABYTES = 16;
            internal const int crypto_aead_aes256gcm_KEYBYTES = 32;
            internal const int crypto_aead_aes256gcm_NPUBBYTES = 12;
            internal const int crypto_aead_aes256gcm_NSECBYTES = 0;

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_aead_aes256gcm_decrypt(
                byte* m,
                out ulong mlen_p,
                byte* nsec,
                byte* c,
                ulong clen,
                byte* ad,
                ulong adlen,
                byte* npub,
                byte* k);

            [DllImport(Libraries.Libsodium, CallingConvention = CallingConvention.Cdecl)]
            internal static unsafe extern int crypto_aead_aes256gcm_encrypt(
                byte* c,
                out ulong clen_p,
                byte* m,
                ulong mlen,
                byte* ad,
                ulong adlen,
                byte* nsec,
                byte* npub,
                byte* k);
        }
    }
}