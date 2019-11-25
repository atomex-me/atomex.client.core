using System;
using System.Collections.Generic;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;

namespace Atomex.Wallet
{
    public class AssetWarrantyManager : IAssetWarrantyManager
    {
        private class AssetWarranty
        {
            public string Address { get; set; }
            public string Currency { get; set; }
            public decimal Value { get; set; }
        }

        private IDictionary<Tuple<string, string>, AssetWarranty> _warranties;

        public AssetWarrantyManager()
        {
            _warranties = new Dictionary<Tuple<string, string>, AssetWarranty>();
        }

        public bool Alloc(WalletAddress address, decimal value)
        {
            var warrantyKey = Tuple.Create(address.Address, address.Currency.Name);

            lock (_warranties)
            {
                if (_warranties.TryGetValue(warrantyKey, out var warranty))
                {
                    if (warranty.Value + value > address.AvailableBalance())
                        return false;

                    warranty.Value += value;
                }
                else
                {
                    _warranties.Add(warrantyKey, new AssetWarranty
                    {
                        Address = address.Address,
                        Currency = address.Currency.Name,
                        Value = value
                    });
                }
            }

            return true;
        }

        public bool Dealloc(WalletAddress address, decimal value)
        {
            var warrantyKey = Tuple.Create(address.Address, address.Currency.Name);

            lock (_warranties)
            {
                if (_warranties.TryGetValue(warrantyKey, out var warranty))
                {
                    warranty.Value -= value;

                    if (warranty.Value <= 0)
                        _warranties.Remove(warrantyKey);
                }
                else return false;
            }

            return true;
        }

        public decimal Locked(WalletAddress address)
        {
            var warrantyKey = Tuple.Create(address.Address, address.Currency.Name);

            lock (_warranties)
                if (_warranties.TryGetValue(warrantyKey, out var warranty))
                    return warranty.Value;

            return 0;
        }
    }
}