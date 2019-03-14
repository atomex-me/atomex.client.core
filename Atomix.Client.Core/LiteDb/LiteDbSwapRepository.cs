using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Atomix.Swaps;
using Atomix.Swaps.Abstract;
using LiteDB;
using Serilog;

namespace Atomix.LiteDb
{
    public class LiteDbSwapRepository : LiteDbRepository, ISwapRepository
    {
        public const string SwapsCollectionName = "swaps";

        private readonly ConcurrentDictionary<Guid, ISwapState> _swapById = new ConcurrentDictionary<Guid, ISwapState>();

        private bool _loaded;

        public LiteDbSwapRepository(string pathToDb, SecureString password)
            : base(pathToDb, password)
        {
        }

        public Task<bool> AddSwapAsync(ISwapState swap)
        {
            if (!_swapById.TryAdd(swap.Id, swap))
                return Task.FromResult(false);

            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var swaps = db.GetCollection<SwapState>(SwapsCollectionName);

                    swaps.Insert((SwapState)swap);
                }

                return Task.FromResult(true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap add error");
            }

            return Task.FromResult(false);
        }

        public Task<bool> UpdateSwapAsync(ISwapState swap)
        {
            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var swaps = db.GetCollection<SwapState>(SwapsCollectionName);

                    return Task.FromResult(swaps.Update((SwapState)swap));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap update error");
            }

            return Task.FromResult(false);
        }

        public Task<bool> RemoveSwapAsync(ISwapState swap)
        {
            if (!_swapById.TryRemove(swap.Id, out _))
                return Task.FromResult(false);

            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var swaps = db.GetCollection<SwapState>(SwapsCollectionName);

                    return Task.FromResult(swaps.Delete(swap.Id));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swap removal error");
            }

            return Task.FromResult(false);
        }

        public Task<ISwapState> GetSwapByIdAsync(Guid id)
        {
            if (_swapById.TryGetValue(id, out var swap))
                return Task.FromResult(swap);

            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var swapCollection = db.GetCollection<SwapState>(SwapsCollectionName);

                    swap = swapCollection.FindById(id);

                    if (swap != null)
                    {
                        _swapById.TryAdd(swap.Id, swap);
                        return Task.FromResult(swap);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error getting swap by id");
            }

            return Task.FromResult<ISwapState>(null);
        }

        public Task<IEnumerable<ISwapState>> GetSwapsAsync()
        {
            if (_loaded)
                return Task.FromResult<IEnumerable<ISwapState>>(_swapById.Values);

            try
            {
                using (var db = new LiteDatabase(ConnectionString))
                {
                    var swapCollection = db.GetCollection<SwapState>(SwapsCollectionName);

                    var swaps = swapCollection.Find(Query.All());

                    foreach (var swap in swaps)
                        if (!_swapById.ContainsKey(swap.Id))
                            _swapById.TryAdd(swap.Id, swap);
  
                    _loaded = true;

                    return Task.FromResult<IEnumerable<ISwapState>>(_swapById.Values);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Swaps getting error");
            }

            return Task.FromResult(Enumerable.Empty<ISwapState>());
        }
    }
}