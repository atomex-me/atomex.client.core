using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace Atomex.Common
{
    public class SecureBytes : IDisposable
    {
        private const string EncodingTable = "0123456789ABCDEF";
        private SecureString _data;

        private SecureBytes(SecureString secureString)
        {
            _data = secureString.Copy();
        }

        public SecureBytes(byte[] bytes)
        {
            _data = BytesToHexSecureString(bytes);
        }

        public SecureBytes(ScopedBytes scopedBytes)
            : this(scopedBytes.Data)
        {
        }

        public ScopedBytes ToUnsecuredBytes()
        {
            var bytes = new byte[_data.Length / 2];

            var bstr = IntPtr.Zero;

            RuntimeHelpers.PrepareConstrainedRegions();

            try
            {
                bstr = bstr = Marshal.SecureStringToBSTR(_data);

                var i = 0;

                unsafe
                {
                    for (var ptr = (char*)bstr.ToPointer(); *ptr != 0;)
                    {
                        bytes[i] = (byte)(HexCharToByte(*ptr) << 4);
                        ++ptr;

                        bytes[i] |= HexCharToByte(*ptr);
                        ++ptr;
                        ++i;
                    }
                }
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr);
            }

            return new ScopedBytes(bytes);
        }

        public void Reset(byte[] bytes)
        {
            _data.Clear();
            _data.Dispose();

            _data = BytesToHexSecureString(bytes);
        }

        public SecureBytes Clone()
        {
            return new SecureBytes(_data);
        }

        public void Dispose()
        {
            _data.Clear();
            _data.Dispose();
        }

        private static byte HexCharToByte(char c) =>
            c >= 65
                ? (byte)(c - 65 + 10)  // ascii 'A'
                : (byte)(c - 48); // ascii '0'

        private static SecureString BytesToHexSecureString(byte[] bytes)
        {
            var result = new SecureString();

            foreach (var @byte in bytes)
            {
                char a = EncodingTable[(@byte >> 4) & 0x0F];
                char b = EncodingTable[@byte & 0x0F];

                result.AppendChar(a);
                result.AppendChar(b);
            }

            return result;
        }
    }
}