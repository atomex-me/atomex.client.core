using System;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Wallet
{
    public class BalanceUpdateTask : BackgroundTask
    {
        public IAccount Account { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                await new HdWalletScanner(Account)
                    .ScanFreeAddressesAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Balance autoupdate task error");

                //ErrorHandler?.Invoke(this);
            }

            return false;
        }
    }
}