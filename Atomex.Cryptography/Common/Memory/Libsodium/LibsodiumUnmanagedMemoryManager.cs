using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

using Atomex.Common.Libsodium;
using Atomex.Common.Memory.Abstract;
using static Atomex.Common.Libsodium.Interop.Libsodium;

namespace Atomex.Common.Memory.Libsodium
{
    internal class LibsodiumUnmanagedMemoryManager<T> : MemoryManager<T>, IUnmanagedMemoryManager<T>
        where T : unmanaged
    {
        private IntPtr _ptr;
        private bool disposed;

        public LibsodiumUnmanagedMemoryManager(int length)
        {
            Debug.Assert(Sodium.IsInitialized);

            var ptr = sodium_malloc((UIntPtr)checked(length * Unsafe.SizeOf<T>()));

            if (ptr == IntPtr.Zero)
                throw new OutOfMemoryException();

            _ptr = ptr;
            Length = length;
        }

        public int Length { get; private set; }

        public unsafe override Span<T> GetSpan() =>
            new(_ptr.ToPointer(), Length);

        public unsafe ReadOnlySpan<T> GetReadOnlySpan() =>
            new(_ptr.ToPointer(), Length);

        public Memory<T> GetMemory() => Memory;

        public ReadOnlyMemory<T> GetReadOnlyMemory() => Memory;

        public unsafe override MemoryHandle Pin(int elementIndex = 0)
        {
            var ptr = (void*)_ptr;

            if (ptr == null)
                throw new ObjectDisposedException(GetType().FullName);

            if (unchecked((uint)elementIndex > (uint)Length))
                throw new ArgumentOutOfRangeException(nameof(elementIndex));

            return new MemoryHandle(Unsafe.Add<T>(ptr, elementIndex), default, this);
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
                return;

            // zero memory
            if (_ptr != IntPtr.Zero && Length > 0)
                Utils.Memset<T>(_ptr, (uint)Length, 0x00);

            sodium_free(Interlocked.Exchange(ref _ptr, IntPtr.Zero));

            Length = 0;
            disposed = true;
        }

        //~LibsodiumUnmanagedMemoryManager()
        //{
        //    Dispose(disposing: false);
        //}

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}