using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallets.Tezos.Common
{
	/// <summary>
	/// Basic async collection interface
	/// </summary>
	/// <typeparam name="T">Item type</typeparam>
	public interface IAsyncCollection<T> : IEnumerable<T>
	{
		/// <summary>
		/// Gets the number of elements in collection
		/// </summary>
		int Count { get; }
		/// <summary>
		/// Add item to queue
		/// </summary>
		/// <param name="item">New item</param>
		void Add(T item);
		/// <summary>
		/// Tries to remove and return the item from the beginning of the queue without blocking
		/// </summary>
		/// <param name="cancellationToken">Cancellation token</param>
		/// <returns>First item</returns>
		Task<T> TakeAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Async queue collection
	/// </summary>
	/// <typeparam name="T">Item type</typeparam>
	public class AsyncQueue<T> : IAsyncCollection<T>
	{
		private readonly ConcurrentQueue<T> _itemQueue = new();
		private readonly ConcurrentQueue<TaskCompletionSource<T>> _awaiterQueue = new();

		private long _queueBalance = 0;

		/// <inheritdoc/>
		public int Count => _itemQueue.Count;

		/// <summary>
		/// Gets async queue enumerator
		/// </summary>
		/// <returns></returns>
		public IEnumerator<T> GetEnumerator() => _itemQueue.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		/// <inheritdoc/>
		public void Add(T item)
		{
			while (!TryAdd(item));
		}

		/// <summary>
		/// Clear queue
		/// </summary>
		public void Clear()
		{
			_itemQueue.Clear();
		}

		/// <inheritdoc/>
		public Task<T> TakeAsync(CancellationToken cancellationToken = default)
		{
			var balanceAfterCurrentAwaiter = Interlocked.Decrement(ref _queueBalance);

			if (balanceAfterCurrentAwaiter < 0)
			{
				var taskSource = new TaskCompletionSource<T>();
				_awaiterQueue.Enqueue(taskSource);

				cancellationToken.Register(
					state =>
					{
						var awaiter = state as TaskCompletionSource<T>;
						awaiter.TrySetCanceled();
					},
					taskSource,
					useSynchronizationContext: false);

				return taskSource.Task;
			}
			else
			{
				T item;
				var spin = new SpinWait();

				while (!_itemQueue.TryDequeue(out item))
					spin.SpinOnce();

				return Task.FromResult(item);
			}
		}

		/// <summary>
		/// Try to return an item from the beginning of the queue without removing it
		/// </summary>
		/// <param name="item">Item</param>
		/// <returns>True if success, otherwise false</returns>
		public bool TryPeek(out T item) =>
			_itemQueue.TryPeek(out item);

		private bool TryAdd(T item)
		{
			var balanceAfterCurrentItem = Interlocked.Increment(ref _queueBalance);

			if (balanceAfterCurrentItem > 0)
			{
				_itemQueue.Enqueue(item);
				return true;
			}
			else
			{
				TaskCompletionSource<T> awaiter;
				var spin = new SpinWait();

				while (!_awaiterQueue.TryDequeue(out awaiter))
					spin.SpinOnce();

				return awaiter.TrySetResult(item);
			}
		}
	}
}