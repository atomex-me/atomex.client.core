using Atomex.Core.Entities;

namespace Atomex.Wallet.Abstract
{
    public interface IAssetWarrantyManager
    {
        bool Alloc(WalletAddress address, decimal value);
        bool Dealloc(WalletAddress address, decimal value);
        decimal Locked(WalletAddress address);
    }
}