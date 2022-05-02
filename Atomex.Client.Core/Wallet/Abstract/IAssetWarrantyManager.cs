using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IAssetWarrantyManager
    {
        bool Alloc(WalletAddress_OLD address, decimal value);
        bool Dealloc(WalletAddress_OLD address, decimal value);
        decimal Locked(WalletAddress_OLD address);
    }
}