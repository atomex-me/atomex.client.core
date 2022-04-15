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
            var bstrPtr = IntPtr.Zero;

            if (secureString != null)
            {
                if (secureString.Length != 0)
                {
                    try
                    {
                        bstrPtr = Marshal.SecureStringToBSTR(secureString);
                        return Marshal.PtrToStringBSTR(bstrPtr);
                    }
                    finally
                    {
                        if (bstrPtr != IntPtr.Zero)
                            Marshal.ZeroFreeBSTR(bstrPtr);
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

            var bstr1 = IntPtr.Zero;
            var bstr2 = IntPtr.Zero;
            
            try
            {
                bstr1 = Marshal.SecureStringToBSTR(s1);
                bstr2 = Marshal.SecureStringToBSTR(s2);

                unsafe
                {
                    for (char* ptr1 = (char*)bstr1.ToPointer(), ptr2 = (char*)bstr2.ToPointer(); *ptr1 != 0 && *ptr2 != 0; ++ptr1, ++ptr2)
                        if (*ptr1 != *ptr2)
                            return false;
                }

                return true;
            }
            finally
            {
                if (bstr1 != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr1);
                if (bstr2 != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr2);
            }
        }

        public static void CopyInto(this SecureString source, SecureString destination)
        {
            destination.Clear();

            var bsrc = IntPtr.Zero;

            try
            {
                bsrc = Marshal.SecureStringToBSTR(source);

                unsafe
                {
                    for (var ptr = (char*)bsrc.ToPointer(); *ptr != 0 ; ++ptr)
                        destination.AppendChar(*ptr);
                }

            }
            finally
            {
                if (bsrc != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bsrc);
            }
        }

        public static bool ContainsChar(this SecureString s, Func<char, bool> condition)
        {
            var bstr = IntPtr.Zero;

            try
            {
                bstr = Marshal.SecureStringToBSTR(s);

                unsafe
                {
                    for (var ptr = (char*)bstr.ToPointer(); *ptr != 0; ++ptr)
                        if (condition(* ptr))
                            return true;
                }

                return false;
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(bstr);
            }
        }

        public static bool ContainsChar(this SecureString s, char ch)
        {
            return s.ContainsChar(c => c == ch);
        }

        public static bool ContainsDigit(this SecureString s)
        {
            return s.ContainsChar(char.IsDigit);
        }

        public static bool ContainsLower(this SecureString s)
        {
            return s.ContainsChar(char.IsLower);
        }

        public static bool ContainsUpper(this SecureString s)
        {
            return s.ContainsChar(char.IsUpper);
        }

        public const string SpecialsCharacters = "!@#$%^&*?_~-£()";

        public static bool ContainsSpecials(this SecureString s)
        {
            return s.ContainsChar(c => SpecialsCharacters.Contains(c));
        }

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