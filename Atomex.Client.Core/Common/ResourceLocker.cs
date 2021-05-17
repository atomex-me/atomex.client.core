using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
    public class ResourceLock<T> : IDisposable
    {
        private readonly T _resource;
        private ResourceLocker<T> _locker;

        public ResourceLock(ResourceLocker<T> locker, T resource)
        {
            _locker = locker;
            _resource = resource;
        }

        public void Dispose()
        {
            if (_locker != null)
            {
                _locker.Unlock(_resource);
                _locker = null;
            }
        }
    }

    public class ResourceLocker<T> : IDisposable
    {
        private readonly ConcurrentDictionary<T, SemaphoreSlim> _semaphores;

        public ResourceLocker()
        {
            _semaphores = new ConcurrentDictionary<T, SemaphoreSlim>();
        }

        /// <summary>
        /// Locks resource. Other threads calling this method will wait for unlock
        /// </summary>
        /// <param name="resource">Resource</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public Task LockAsync(
            T resource,
            CancellationToken cancellationToken = default)
        {
            SemaphoreSlim createdSemaphore = null;

            var semaphore = _semaphores.GetOrAdd(resource, (addr) => createdSemaphore = new SemaphoreSlim(1));

            // check if semaphore was created redundantly and if so => clean up
            if (semaphore != createdSemaphore)
                createdSemaphore?.Dispose();

            return semaphore.WaitAsync(cancellationToken);
        }

        public async Task<ResourceLock<T>> GetLockAsync(
            T resource,
            CancellationToken cancellationToken = default)
        {
            await LockAsync(resource, cancellationToken);

            return new ResourceLock<T>(this, resource);
        }

        /// <summary>
        /// Unlock resource, if exists
        /// </summary>
        /// <param name="resource">Resource</param>
        public void Unlock(T resource)
        {
            if (!_semaphores.TryGetValue(resource, out var semaphore))
                return;

            semaphore.Release();
        }

        public void Dispose()
        {
            foreach (var semaphore in _semaphores.Values)
                semaphore.Dispose();

            _semaphores.Clear();
        }
    }
}