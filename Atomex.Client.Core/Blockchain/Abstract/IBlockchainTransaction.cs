using Atomex.Core.Entities;

namespace Atomex.Blockchain.Abstract
{
    public interface IBlockchainTransaction
    {
        string Id { get; }
        Currency Currency { get; }
        BlockInfo BlockInfo { get; }

        bool IsConfirmed();
    }
}