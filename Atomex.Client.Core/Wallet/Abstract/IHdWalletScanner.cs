using System.Threading;
using System.Threading.Tasks;
using Atomex.Core.Entities;

namespace Atomex.Wallet.Abstract
{
    public interface IHdWalletScanner
    {
        Task ScanAsync(
            Currency currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken));

        Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken));

        Task ScanFreeAddressesAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task ScanAddressAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}