using System;
using System.Collections.Generic;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;

namespace Atomex.Common
{
    public class AvailableBalanceAscending : IComparer<WalletAddress>
    {
        private readonly IAssetWarrantyManager _assetWarrantyManager;

        public AvailableBalanceAscending(IAssetWarrantyManager assetWarrantyManager)
        {
            _assetWarrantyManager = assetWarrantyManager ?? throw new ArgumentNullException(nameof(assetWarrantyManager));
        }

        public int Compare(WalletAddress x, WalletAddress y)
        {
            return x.AvailableBalance(_assetWarrantyManager).CompareTo(y.AvailableBalance(_assetWarrantyManager));
        }
    }

    public class AvailableBalanceDescending : IComparer<WalletAddress>
    {
        private readonly IAssetWarrantyManager _assetWarrantyManager;

        public AvailableBalanceDescending(IAssetWarrantyManager assetWarrantyManager)
        {
            _assetWarrantyManager = assetWarrantyManager ?? throw new ArgumentNullException(nameof(assetWarrantyManager));
        }

        public int Compare(WalletAddress x, WalletAddress y)
        {
            return y.AvailableBalance(_assetWarrantyManager).CompareTo(x.AvailableBalance(_assetWarrantyManager));
        }
    }
}