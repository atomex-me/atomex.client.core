using NBitcoin;

namespace Atomex.Cryptography
{
    public interface IExtKey : IKey
    {
        IExtKey Derive(uint index);
        IExtKey Derive(KeyPath keyPath);
    }
}