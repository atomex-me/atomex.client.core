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
                where TWalletScanner : ICurrencyHdWalletScanner_OLD
                where TCurrencyAccount : ICurrencyAccount_OLD
        {
            return Task.Run(async () =>
            {
                try
                {
                    var scanner = (ICurrencyHdWalletScanner_OLD)Activator.CreateInstance(typeof(TWalletScanner), account);

                    await scanner
                        .ScanAsync(address, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("UpdateAddressBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Address balance update task error.");
                }

            }, cancellationToken);
        }

        public static Task UpdateAddressBalanceAsync<TWalletScanner, TCurrencyAccount, TBaseCurrencyAccount>(
            TCurrencyAccount account,
            TBaseCurrencyAccount baseAccount,
            string address,
            CancellationToken cancellationToken = default)
                where TWalletScanner : ICurrencyHdWalletScanner_OLD
                where TCurrencyAccount : ICurrencyAccount_OLD
                where TBaseCurrencyAccount : ICurrencyAccount_OLD
        {
            return Task.Run(async () =>
            {
                try
                {
                    var scanner = (ICurrencyHdWalletScanner_OLD)Activator.CreateInstance(
                        typeof(TWalletScanner),
                        account,
                        baseAccount);

                    await scanner
                        .ScanAsync(address, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("UpdateAddressBalanceAsync canceled.");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Address balance update task error.");
                }

            }, cancellationToken);
        }
    }
}