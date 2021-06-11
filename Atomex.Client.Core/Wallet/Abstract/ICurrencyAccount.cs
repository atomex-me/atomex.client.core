using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Blockchain;
using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface ICurrencyAccount
    {
        event EventHandler<CurrencyEventArgs> BalanceUpdated;
        event EventHandler<TransactionEventArgs> UnconfirmedTransactionAdded;

        #region Balances

        Balance GetBalance();

        Task<Balance> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string address,
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
    }
}