using Atomix.Core.Entities;

namespace Atomix.Blockchain.Abstract
{
    public interface IBlockchainTransaction
    {
        string Id { get; }
        Currency Currency { get; }
        BlockInfo BlockInfo { get; }

        bool IsConfirmed();
    }
}