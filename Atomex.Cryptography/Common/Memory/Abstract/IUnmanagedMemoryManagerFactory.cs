namespace Atomex.Common.Memory.Abstract
{
    internal interface IUnmanagedMemoryManagerFactory<T>
        where T : unmanaged
    {
        IUnmanagedMemoryManager<T> Create(int length);
    }
}