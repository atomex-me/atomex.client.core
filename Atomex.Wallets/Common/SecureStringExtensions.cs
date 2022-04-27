using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Atomex.Common
{
    public static class SecureStringExtensions
    {
        public static SecureString ToSecureString(this string s)
        {
            var ss = new SecureString();

            foreach (var c in s)
                ss.AppendChar(c);

            return ss;
        }

        public static string ToUnsecuredString(this SecureString secureString)
        {
            var unistr = IntPtr.Zero;

            if (secureString != null)
            {
                if (secureString.Length != 0)
                {
                    try
                    {
                        unistr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                        return Marshal.PtrToStringUni(unistr);
                    }
                    finally
                    {
                        if (unistr != IntPtr.Zero)
                            Marshal.ZeroFreeGlobalAllocUnicode(unistr);
                    }
                }
            }

            return string.Empty;
        }

        public static bool SecureEqual(this SecureString s1, SecureString s2)
        {
            if (s1 == null)
                throw new ArgumentNullException(nameof(s1));

            if (s2 == null)
                throw new ArgumentNullException(nameof(s2));

            if (s1.Length != s2.Length)
                return false;

            var unistr1 = IntPtr.Zero;
            var unistr2 = IntPtr.Zero;

            try
            {
                unistr1 = Marshal.SecureStringToGlobalAllocUnicode(s1);
                unistr2 = Marshal.SecureStringToGlobalAllocUnicode(s2);

                unsafe
                {
                    for (char* ptr1 = (char*)unistr1.ToPointer(), ptr2 = (char*)unistr2.ToPointer(); *ptr1 != 0 && *ptr2 != 0; ++ptr1, ++ptr2)
                        if (*ptr1 != *ptr2)
                            return false;
                }

                return true;
            }
            finally
            {
                if (unistr1 != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(unistr1);
                if (unistr2 != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(unistr2);
            }
        }

        public static void CopyInto(this SecureString source, SecureString destination)
        {
            destination.Clear();

            var unistr = IntPtr.Zero;

            try
            {
                unistr = Marshal.SecureStringToGlobalAllocUnicode(source);

                unsafe
                {
                    for (var ptr = (char*)unistr.ToPointer(); *ptr != 0; ++ptr)
                        destination.AppendChar(*ptr);
                }

            }
            finally
            {
                if (unistr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(unistr);
            }
        }

        public static bool ContainsChar(this SecureString s, Func<char, bool> condition)
        {
            var unistr = IntPtr.Zero;

            try
            {
                unistr = Marshal.SecureStringToGlobalAllocUnicode(s);

                unsafe
                {
                    for (var ptr = (char*)unistr.ToPointer(); *ptr != 0; ++ptr)
                        if (condition(*ptr))
                            return true;
                }

                return false;
            }
            finally
            {
                if (unistr != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(unistr);
            }
        }

        public static bool ContainsChar(this SecureString s, char ch) =>
            s.ContainsChar(c => c == ch);

        public static bool ContainsDigit(this SecureString s) =>
            s.ContainsChar(char.IsDigit);

        public static bool ContainsLower(this SecureString s) =>
            s.ContainsChar(char.IsLower);

        public static bool ContainsUpper(this SecureString s) =>
            s.ContainsChar(char.IsUpper);

        public const string SpecialsCharacters = "!@#$%^&*?_~-£()";

        public static bool ContainsSpecials(this SecureString s) =>
            s.ContainsChar(c => SpecialsCharacters.Contains(c));

        public static byte[] ToBytes(this SecureString s)
        {
            var result = new byte[s.Length];

            var unmanagedBytes = Marshal.SecureStringToGlobalAllocAnsi(s);

            try
            {
                unsafe
                {
                    var byteArray = (byte*)unmanagedBytes.ToPointer();

                    for (var i = 0; i < s.Length; ++i)
                        result[i] = *(byteArray + i);
                }
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocAnsi(unmanagedBytes);
            }

            return result;
        }
    }
}