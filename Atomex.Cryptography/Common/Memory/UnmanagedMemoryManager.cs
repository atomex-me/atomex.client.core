using System;
using System.Buffers;

using Atomex.Common.Libsodium;
using Atomex.Common.Memory.Abstract;
using Atomex.Common.Memory.DotNet;
using Atomex.Common.Memory.Libsodium;

namespace Atomex.Common.Memory
{
    /// <summary>
    /// Provides unmanaged memory control and memory protection against unloading to page file on platforms that support Libsodium
    /// </summary>
    /// <typeparam name="T">Unmanaged data type</typeparam>
    public class UnmanagedMemoryManager<T> : MemoryManager<T>, IUnmanagedMemoryManager<T>
        where T : unmanaged
    {
        private static readonly IUnmanagedMemoryManagerFactory<T> _implFactory;
        private readonly IUnmanagedMemoryManager<T> _impl;

        static UnmanagedMemoryManager()
        {
            _implFactory = Sodium.IsInitialized
                ? (IUnmanagedMemoryManagerFactory<T>)new LibsodiumUnmanagedMemoryManagerFactory<T>()
                : (IUnmanagedMemoryManagerFactory<T>)new DotNetUnmanagedMemoryManagerFactory<T>();
        }

        public UnmanagedMemoryManager(int length)
        {
            _impl = _implFactory.Create(length);
        }

        public UnmanagedMemoryManager(ReadOnlySpan<T> data)
        {
            _impl = _implFactory.Create(data.Length);

            data.CopyTo(GetSpan());
        }

        public int Length => _impl.Length;

        public override Span<T> GetSpan() => _impl.GetSpan();

        public ReadOnlySpan<T> GetReadOnlySpan() => _impl.GetReadOnlySpan();

        public Memory<T> GetMemory() => _impl.GetMemory();

        public ReadOnlyMemory<T> GetReadOnlyMemory() => _impl.GetReadOnlyMemory();

        public void CopyTo(Span<T> span) => GetReadOnlySpan().CopyTo(span);

        public override MemoryHandle Pin(int elementIndex = 0) => _impl.Pin(elementIndex);

        public override void Unpin() => _impl.Unpin();

        public T this[int index]
        {
            get => GetReadOnlySpan()[index];
            set { GetSpan()[index] = value; }
        }

        public static implicit operator ReadOnlySpan<T>(UnmanagedMemoryManager<T> memoryManager) =>
            memoryManager.GetReadOnlySpan();

        public static implicit operator Span<T>(UnmanagedMemoryManager<T> memoryManager) =>
            memoryManager.GetSpan();

        private bool disposed;

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                    _impl.Dispose();

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}