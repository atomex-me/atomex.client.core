using System;

namespace Atomex.Common.Memory
{
    /// <summary>
    /// Provides unmanaged bytes, protected against unloading to page file on platforms that support Libsodium
    /// </summary>
    public class UnmanagedBytes : UnmanagedMemoryManager<byte>
    {
        public UnmanagedBytes(int length)
            : base(length)
        {
        }

        public UnmanagedBytes(ReadOnlySpan<byte> data)
            : base(data)
        { 
        }

        public byte[] ToBytes() => GetReadOnlySpan().ToArray();
    }
}