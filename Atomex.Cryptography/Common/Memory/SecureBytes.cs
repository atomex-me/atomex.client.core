using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Atomex.Common.Memory
{
    /// <summary>
    /// Represents bytes that should be kept confidential, such as by deleting it from computer memory when no longer needed. This class cannot be inherited.
    /// </summary>
    public sealed class SecureBytes : IDisposable
    {
        private SecureString _securedData;

        private SecureBytes(SecureString secureString)
        {
            _securedData = secureString.Copy();
        }

        public SecureBytes(ReadOnlySpan<byte> data)
        {
            _securedData = data.ToHexSecureString();
        }

        public SecureBytes(UnmanagedBytes unmanagedBytes)
            : this(unmanagedBytes.GetReadOnlySpan())
        {
        }

        public int Length => _securedData.Length / 2;

        public unsafe UnmanagedBytes ToUnmanagedBytes()
        {
            var bytesCount = _securedData.Length / 2;
            var bytes = new UnmanagedBytes(bytesCount);
            var bytesSpan = bytes.GetSpan();
            var unistr = IntPtr.Zero;

            try
            {
                unistr = Marshal.SecureStringToGlobalAllocUnicode(_securedData);

                var bstrPtr = (char*)unistr.ToPointer();

                for (var i = 0; i < bytesCount; ++i)
                {
                    bytesSpan[i] = (byte)(HexCharToByte(*bstrPtr) << 4);
                    ++bstrPtr;

                    bytesSpan[i] |= HexCharToByte(*bstrPtr);
                    ++bstrPtr;
                }
            }
            finally
            {
                if (unistr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(unistr);
            }

            return bytes;
        }

        public byte[] ToUnsecuredBytes()
        {
            using var unmanagedBytes = ToUnmanagedBytes();

            return unmanagedBytes.ToBytes();
        }

        public void Reset(ReadOnlySpan<byte> data)
        {
            _securedData.Clear();
            _securedData.Dispose();

            _securedData = data.ToHexSecureString();
        }

        public SecureBytes Copy()
        {
            return new SecureBytes(_securedData);
        }

        public void Dispose()
        {
            _securedData.Clear();
            _securedData.Dispose();
        }

        private static byte HexCharToByte(char c) =>
            c >= 65
                ? (byte)(c - 65 + 10)  // ascii 'A'
                : (byte)(c - 48); // ascii '0'
    }
}