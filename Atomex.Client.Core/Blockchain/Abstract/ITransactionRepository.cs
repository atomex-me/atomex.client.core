using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Core.Entities;

namespace Atomex.Blockchain.Abstract
{
    public interface ITransactionRepository
    {
        Task AddTransactionAsync(IBlockchainTransaction tx);
        Task<bool> AddOutputsAsync(IEnumerable<ITxOutput> outputs, Currency currency, string address);
        Task<IBlockchainTransaction> GetTransactionByIdAsync(Currency currency, string txId);
        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(Currency currency);
        Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(Currency currency, bool skipUnconfirmed = true);
        Task<IEnumerable<ITxOutput>> GetUnspentOutputsAsync(Currency currency, string address, bool skipUnconfirmed = true);
        Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency);
        Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency, string address);
    }
}