using System;
using System.Buffers;

namespace Atomex.Common.Memory.Abstract
{
    internal interface IUnmanagedMemoryManager<T> : IDisposable
        where T : unmanaged
    {
        int Length { get; }
        Span<T> GetSpan();
        ReadOnlySpan<T> GetReadOnlySpan();
        Memory<T> GetMemory();
        ReadOnlyMemory<T> GetReadOnlyMemory();
        MemoryHandle Pin(int elementIndex = 0);
        void Unpin();
    }
}