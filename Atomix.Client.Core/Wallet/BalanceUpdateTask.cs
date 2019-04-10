using System;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Wallet
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