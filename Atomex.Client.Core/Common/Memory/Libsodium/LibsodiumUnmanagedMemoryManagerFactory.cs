using Atomex.Common.Memory.Abstract;

namespace Atomex.Common.Memory.Libsodium
{
    internal class LibsodiumUnmanagedMemoryManagerFactory<T> : IUnmanagedMemoryManagerFactory<T>
        where T : unmanaged
    {
        public IUnmanagedMemoryManager<T> Create(int length)
            => new LibsodiumUnmanagedMemoryManager<T>(length);
    }
}