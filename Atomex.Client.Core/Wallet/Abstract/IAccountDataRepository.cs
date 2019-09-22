using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain.Abstract;
using Atomex.Core.Entities;

namespace Atomex.Wallet.Abstract
{
    public interface IAccountDataRepository
    {
        #region Addresses

        Task<bool> UpsertAddressAsync(WalletAddress walletAddress);
        Task<int> UpsertAddressesAsync(IEnumerable<WalletAddress> walletAddresses);
        Task<bool> TryInsertAddressAsync(WalletAddress walletAddress);
        Task<WalletAddress> GetWalletAddressAsync(Currency currency, string address);
        Task<WalletAddress> GetLastActiveWalletAddressAsync(Currency currency, int chain);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            Currency currency,
            bool includeUnconfirmed = true);

        #endregion Addresses

        #region Transactions

        Task<bool> UpsertTransactionAsync(IBlockchainTransaction tx);
        Task<IBlockchainTransaction> GetTransactionByIdAsync(Currency currency, string txId);
        Task<IEnumerable<IBlockchainTransaction>> GetTransactionsAsync(Currency currency);
        Task<IEnumerable<IBlockchainTransaction>> GetUnconfirmedTransactionsAsync(Currency currency);
        Task<bool> RemoveTransactionByIdAsync(string id);

        #endregion Transactions

        #region Outputs

        Task<bool> UpsertOutputsAsync(
            IEnumerable<ITxOutput> outputs,
            Currency currency,
            string address);

        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(Currency currency);
        Task<IEnumerable<ITxOutput>> GetAvailableOutputsAsync(Currency currency, string address);
        Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency);
        Task<IEnumerable<ITxOutput>> GetOutputsAsync(Currency currency, string address);

        Task<ITxOutput> GetOutputAsync(
            Currency currency,
            string txId,
            uint index);

        #endregion Outputs

        #region Orders

        Task<bool> UpsertOrderAsync(Order order);
        Order GetOrderById(string clientOrderId);

        #endregion Orders

        #region Swaps

        Task<bool> AddSwapAsync(ClientSwap swap);
        Task<bool> UpdateSwapAsync(ClientSwap swap);
        Task<ClientSwap> GetSwapByIdAsync(long id);
        Task<IEnumerable<ClientSwap>> GetSwapsAsync();

        #endregion Swaps
    }
}