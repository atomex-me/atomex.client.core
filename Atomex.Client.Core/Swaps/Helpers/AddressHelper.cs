using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Swaps.Helpers
{
    public static class AddressHelper
    {
        public static Task UpdateAddressBalanceAsync<TWalletScanner, TCurrencyAccount>(
            TCurrencyAccount account,
            string address,
            CancellationToken cancellationToken = default)
                where TWalletScanner : ICurrencyHdWalletScanner
                where TCurrencyAccount : ICurrencyAccount
        {
            return Task.Run(async () =>
            {
                try
                {
                    var scanner = (ICurrencyHdWalletScanner)Activator.CreateInstance(typeof(TWalletScanner), account);

                    await scanner
                        .ScanAsync(address, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error("Address balance update task error");
                }
            }, cancellationToken);
        }

        public static Task UpdateAddressBalanceAsync<TWalletScanner, TCurrencyAccount, TBaseCurrencyAccount>(
            TCurrencyAccount account,
            TBaseCurrencyAccount baseAccount,
            string address,
            CancellationToken cancellationToken = default)
                where TWalletScanner : ICurrencyHdWalletScanner
                where TCurrencyAccount : ICurrencyAccount
                where TBaseCurrencyAccount : ICurrencyAccount
        {
            return Task.Run(async () =>
            {
                try
                {
                    var scanner = (ICurrencyHdWalletScanner)Activator.CreateInstance(
                        typeof(TWalletScanner),
                        account,
                        baseAccount);

                    await scanner
                        .ScanAsync(address, cancellationToken)
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