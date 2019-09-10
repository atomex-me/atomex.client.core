using System;
using System.Runtime.InteropServices;
using System.Security;
using Atomix.Cryptography;

namespace Atomix.Blockchain.Tezos.Internal
{
    public class Keys
    {
        private const int PublicKeyHashBitSize = 20 * 8;

        public SecureString PublicKey { get; }
        public SecureString PrivateKey { get; }
        public string PublicHash { get; }

        public Keys()
        { }

        public Keys(byte[] sk, byte[] pk)
        {
            PublicHash = Base58Check.Encode(new HmacBlake2b(PublicKeyHashBitSize).ComputeHash(pk), Prefix.Tz1);

            PublicKey = new SecureString();
            PrivateKey = new SecureString();

            var encodedPk = Base58Check.Encode(pk, Prefix.Edpk);
            foreach (var c in encodedPk)
                PublicKey.AppendChar(c);

            var encodedSk = Base58Check.Encode(sk, Prefix.Edsk);
            foreach (var c in encodedSk)
                PrivateKey.AppendChar(c);

            Array.Clear(pk, 0, pk.Length);
            Array.Clear(sk, 0, sk.Length);
        }

        /// <summary>
        /// Do not store this result on the heap!
        /// </summary>
        /// <returns>Decrypted public key</returns>
        internal string DecryptPublicKey()
        {
            var valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(PublicKey);
                return Marshal.PtrToStringUni(valuePtr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }

        /// <summary>
        /// Do not store this result on the heap!
        /// </summary>
        /// <returns>Decrypted private key</returns>
        internal string DecryptPrivateKey()
        {
            var valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(PrivateKey);
                return Marshal.PtrToStringUni(valuePtr);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }
    }
}