using System.Threading;
using System.Threading.Tasks;

namespace Atomex.Wallet.Abstract
{
    public interface ICurrencyHdWalletScanner
    {
        Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken));

        Task ScanAsync(
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}