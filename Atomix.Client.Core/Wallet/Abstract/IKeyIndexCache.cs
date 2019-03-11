using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public class KeyIndex
    {
        public uint Chain { get; }
        public uint Index { get; }

        public KeyIndex(uint chain, uint index)
        {
            Chain = chain;
            Index = index;
        }
    }

    public interface IKeyIndexCache
    {
        void Add(string address, uint chain, uint index);
        KeyIndex IndexByAddress(WalletAddress walletAddress);
        KeyIndex IndexByAddress(string address);
    }
}