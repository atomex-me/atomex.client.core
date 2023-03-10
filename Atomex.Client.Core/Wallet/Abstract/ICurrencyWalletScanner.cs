using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallet.Abstract
{
    public interface ICurrencyWalletScanner
    {
        Task ScanAsync(
            CancellationToken cancellationToken = default);
        Task UpdateBalanceAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default);
        Task UpdateBalanceAsync(
            string address,
            CancellationToken cancellationToken = default);
    }
}