using Atomex.Common.Memory.Abstract;

namespace Atomex.Common.Memory.DotNet
{
    internal class DotNetUnmanagedMemoryManagerFactory<T> : IUnmanagedMemoryManagerFactory<T>
        where T : unmanaged
    {
        public IUnmanagedMemoryManager<T> Create(int length)
            => new DotNetUnmanagedMemoryManager<T>(length);
    }
}