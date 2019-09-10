using System;
using System.Threading.Tasks;
using Atomex.Common;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Serilog;

namespace Atomex.Wallet
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