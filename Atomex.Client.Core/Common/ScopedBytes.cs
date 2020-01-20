using System;

namespace Atomex.Common
{
    public class ScopedBytes : IDisposable
    {
        public byte[] Data { get; private set; }
        public int Length => Data?.Length ?? 0;

        public ScopedBytes(int length)
        {
            Data = new byte[length];
        }

        public ScopedBytes(byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public byte this[int index]
        {
            get => Data[index];
            set => Data[index] = value;
        }

        public void Dispose()
        {
            Data.Clear();
            Data = null;
        }

        public static implicit operator byte[](ScopedBytes value) => value.Data;
    }
}
