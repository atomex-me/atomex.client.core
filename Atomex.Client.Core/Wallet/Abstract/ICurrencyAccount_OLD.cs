using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface ICurrencyAccount_OLD
    {
        event EventHandler<CurrencyEventArgs> BalanceUpdated;

        #region Balances

        Balance_OLD GetBalance();

        Task<Balance_OLD> GetAddressBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            CancellationToken cancellationToken = default);

        Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);

        #endregion Balances

        #region Addresses

        Task<WalletAddress_OLD> GetAddressAsync(
            string address,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress_OLD>> GetUnspentAddressesAsync(
            CancellationToken cancellationToken = default);
        
        Task<WalletAddress_OLD> GetFreeExternalAddressAsync(
            CancellationToken cancellationToken = default);

        Task<IEnumerable<WalletAddress_OLD>> GetAddressesAsync(
            CancellationToken cancellationToken = default);

        #endregion Addresses
    }
}