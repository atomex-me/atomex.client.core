using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;

namespace Atomex.Wallet.Abstract
{
    public interface IHasTokens
    {
        Task<IEnumerable<WalletAddress_OLD>> GetUnspentTokenAddressesAsync(
            CancellationToken cancellationToken = default);
    }
}