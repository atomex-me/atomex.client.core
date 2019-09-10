using NBitcoin;

namespace Atomix.Cryptography
{
    public interface IExtKey : IKey
    {
        IExtKey Derive(uint index);
        IExtKey Derive(KeyPath keyPath);
    }
}