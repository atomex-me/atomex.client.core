using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Wallets;

namespace Atomex.Wallet.Abstract
{
    public interface IHasTokens
    {
        Task<IEnumerable<WalletAddress>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default);
    }
}