using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core.Entities;
using Atomex.Wallet;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Swaps.Helpers
{
    public static class AddressHelper
    {
        public static Task UpdateAddressBalanceAsync(
            IAccount account,
            Currency currency,
            string address,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await new HdWalletScanner(account)
                        .ScanAddressAsync(currency, address, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Address balance update task error");
                }
            }, cancellationToken);
        }
    }
}