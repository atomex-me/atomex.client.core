using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallet.Abstract
{
    public interface ICurrencyWalletScanner
    {
        Task ScanAsync(
            CancellationToken cancellationToken = default);

        Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default);
    }
}