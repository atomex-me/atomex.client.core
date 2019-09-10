using System;
using System.Threading.Tasks;
using Atomix.Common;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Serilog;

namespace Atomix.Wallet
{
    public class AddressBalanceUpdateTask : BackgroundTask
    {
        public IAccount Account { get; set; }
        public Currency Currency { get; set; }
        public string Address { get; set; }

        public override async Task<bool> CheckCompletion()
        {
            try
            {
                await new HdWalletScanner(Account)
                    .ScanAddressAsync(Currency, Address)
                    .ConfigureAwait(false);

                CompleteHandler?.Invoke(this);
            }
            catch (Exception e)
            {
                Log.Error(e, "Address balance task error");

                ErrorHandler?.Invoke(this);
            }

            return true;
        }
    }
}