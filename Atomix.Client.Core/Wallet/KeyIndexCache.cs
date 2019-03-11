using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Newtonsoft.Json;

namespace Atomix.Wallet
{
    public class KeyIndexCache : IKeyIndexCache
    {
        private readonly ConcurrentDictionary<string, KeyIndex> _cache;

        public KeyIndexCache()
        {
            _cache = new ConcurrentDictionary<string, KeyIndex>();
        }

        public KeyIndexCache(string pathToFile)
        {
            var json = File.ReadAllText(pathToFile);

            _cache = JsonConvert.DeserializeObject<ConcurrentDictionary<string, KeyIndex>>(json);
        }

        public void Add(string address, uint chain, uint index)
        {
            _cache.TryAdd(address, new KeyIndex(chain, index));
        }

        public KeyIndex IndexByAddress(WalletAddress walletAddress)
        {
            return IndexByAddress(walletAddress.Address);
        }

        public KeyIndex IndexByAddress(string address)
        {
            return _cache.TryGetValue(address, out var index)
                ? index
                : null;
        }

        public void SaveToFile(string pathToFile)
        {
            var json = JsonConvert.SerializeObject(_cache);

            File.WriteAllText(pathToFile, json);
        }

        public static KeyIndexCache LoadFromFile(string pathToFile)
        {
            return new KeyIndexCache(pathToFile);
        }
    }
}