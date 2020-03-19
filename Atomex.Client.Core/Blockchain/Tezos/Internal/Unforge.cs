using System;
using System.Linq;
using Atomex.Common;
using Atomex.Cryptography;

namespace Atomex.Blockchain.Tezos.Internal
{
    public class Unforge
    {
        public static string UnforgeAddress(string value)
        {
            var bytes = Hex.FromString(value);

            var prefix = bytes[0];

            if (prefix == 0x00)
            {
                return bytes[1] switch
                {
                    0x00 => Base58Check.Encode(bytes.SubArray(2), Prefix.Tz1),
                    0x01 => Base58Check.Encode(bytes.SubArray(2), Prefix.Tz2),
                    0x02 => Base58Check.Encode(bytes.SubArray(2), Prefix.Tz3),
                    _ => throw new Exception($"Value address exception. Invalid prefix {prefix}"),
                };
            }
            else if (prefix == 0x01)
            {
                if (bytes.Last() == 0x00)
                {
                    return Base58Check.Encode(bytes.SubArray(1, bytes.Length - 2), Prefix.KT);
                }
                else throw new Exception($"Value address exception. Invalid suffix {bytes.Last()}");
            }
            else throw new Exception($"Value address exception. Invalid prefix {prefix}");
        }
    }
}