﻿namespace Atomex.Common
{
    public static class AddressesHelper
    {
        public static string TruncateAddress(this string address, int leftLength = 4, int rightLength = 5)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 9)
                return address;

            return $"{address[..leftLength]}···{address.Substring(address.Length - rightLength, rightLength)}";
        }
    }
}