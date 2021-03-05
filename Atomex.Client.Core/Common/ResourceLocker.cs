using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Common
{
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