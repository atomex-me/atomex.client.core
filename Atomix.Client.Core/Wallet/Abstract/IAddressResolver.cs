using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public interface IAddressResolver
    {
        /// <summary>
        /// Get wallet's address for <paramref name="currency"/> by <paramref name="address"/>
        /// </summary>
        /// <param name="currency">Currency</param>
        /// <param name="address">Address</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Wallet's address</returns>
        Task<WalletAddress> ResolveAddressAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}