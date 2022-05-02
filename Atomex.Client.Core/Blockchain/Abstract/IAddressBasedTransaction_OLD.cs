using System.Threading;
using System.Threading.Tasks;

using Atomex.Core;
using Atomex.Wallet.Abstract;

namespace Atomex.Blockchain.Abstract
{
    public interface IAddressBasedTransaction_OLD : IBlockchainTransaction_OLD
    {
        Task<bool> SignAsync(
            IKeyStorage_OLD keyStorage,
            WalletAddress_OLD address,
            CurrencyConfig currencyConfig,
            CancellationToken cancellationToken = default);
    }
}