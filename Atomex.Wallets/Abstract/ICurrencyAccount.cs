#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using Atomex.Blockchain.Abstract;

namespace Atomex.Wallets.Abstract
{
    public interface ICurrencyAccount
    {
        #region Balances

        Task<Balance> GetBalanceAsync();

        Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            ILogger? logger = null,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string address,
            ILogger? logger = null,
            CancellationToken cancellationToken = default);

        #endregion Balances

        #region Addresses

        Task<WalletAddress> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default);

        Task<WalletAddress> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress>> GetAddressesAsync(
            CancellationToken cancellationToken = default);

        #endregion Addresses

        #region Transactions

        Task<IEnumerable<ITransaction>> GetUnconfirmedTransactionsAsync(
            CancellationToken cancellationToken = default);

        Task<ITransactionMetadata> ResolveTransactionMetadataAsync(
            ITransaction tx,
            CancellationToken cancellationToken = default);

        Task ResolveTransactionsMetadataAsync(
            IEnumerable<ITransaction> txs,
            CancellationToken cancellationToken = default);

        #endregion
    }
}