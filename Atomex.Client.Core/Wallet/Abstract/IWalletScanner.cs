using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallet.Abstract
{
    public interface IWalletScanner
    {
        Task ScanAsync(
            string currency,
            CancellationToken cancellationToken = default);

        Task ScanAsync(
            CancellationToken cancellationToken = default);

        Task ScanAddressAsync(
            string currency,
            string address,
            CancellationToken cancellationToken = default);
    }
}