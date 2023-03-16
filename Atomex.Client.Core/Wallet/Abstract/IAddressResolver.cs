using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;

namespace Atomex.Wallet.Abstract
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
        Task<WalletAddress> GetAddressAsync(
            string currency,
            string address,
            string? tokenContract = null,
            BigInteger? tokenId = null,
            CancellationToken cancellationToken = default);
    }
}