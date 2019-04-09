using System.Threading;
using System.Threading.Tasks;

namespace Atomix.Wallet.Abstract
{
    public interface ICurrencyHdWalletScanner
    {
        int InternalLookAhead { get; set; }
        int ExternalLookAhead { get; set; }

        Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}