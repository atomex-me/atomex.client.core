using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;

namespace Atomix.Wallet.Abstract
{
    public interface IHdWalletScanner : ICurrencyHdWalletScanner
    {
        Task ScanAsync(
            Currency currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}