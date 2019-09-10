using System;
using System.IO;
using System.Linq;

namespace Atomex.Common
{
    public class VarInteger
    {
        public static byte[] GetBytes(ulong value)
        {
            if (value < 0xFD)
                return new[] {(byte)value};

            if (value <= 0xFFFF)
                return new byte[] {0xFD}.Concat(BitConverter.GetBytes((ushort)value)).ToArray();

            if (value <= 0xFFFFFFFF)
                return new byte[] {0xFE}.Concat(BitConverter.GetBytes((uint)value)).ToArray();

            return new byte[] {0xFF}.Concat(BitConverter.GetBytes(value)).ToArray();
        }

        public static ulong GetValue(byte[] bytes)
        {
            return GetValue(bytes, 0, bytes.Length);
        }

        public static ulong GetValue(byte[] bytes, int offset, int length)
        {
            var prefix = bytes[offset];

            if (prefix < 0xFD)
                return prefix;

            if (prefix == 0xFD)
                return BitConverter.ToUInt16(bytes, offset + 1);

            if (prefix == 0xFE)
                return BitConverter.ToUInt32(bytes, offset + 1);

            if (prefix == 0xFF)
                return BitConverter.ToUInt64(bytes, offset + 1);

            throw new Exception($"Invalid VarInteger format. Prefix {prefix}");
        }

        public static ulong GetValue(BinaryReader reader)
        {
            var prefix = reader.ReadByte();

            if (prefix < 0xFD)
                return prefix;

            if (prefix == 0xFD)
                return reader.ReadUInt16();

            if (prefix == 0xFE)
                return reader.ReadUInt32();

            if (prefix == 0xFF)
                return reader.ReadUInt64();

            throw new Exception($"Invalid VarInteger format. Prefix {prefix}");
        }
    }
}