using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Atomex.Common.Memory.Abstract;

namespace Atomex.Common.Memory.DotNet
{
    internal class DotNetUnmanagedMemoryManager<T> : MemoryManager<T>, IUnmanagedMemoryManager<T>
        where T : unmanaged
    {
        private IntPtr _ptr;
        private bool disposed;

        public DotNetUnmanagedMemoryManager(int length)
        {
            var ptr = Marshal.AllocHGlobal(checked(length * Unsafe.SizeOf<T>()));

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
            if (disposed)
                return;

            // zero memory
            if (_ptr != IntPtr.Zero && Length > 0)
                Utils.Memset<T>(_ptr, (uint)Length, 0x00);

            Marshal.FreeHGlobal(Interlocked.Exchange(ref _ptr, IntPtr.Zero));

            Length = 0;
            disposed = true;
        }

        //~DotNetUnmanagedMemoryManager()
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