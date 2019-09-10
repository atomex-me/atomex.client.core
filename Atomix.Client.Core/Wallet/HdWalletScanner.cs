using System;
using System.Threading;
using System.Threading.Tasks;
using Atomix.Core.Entities;
using Atomix.Wallet.Abstract;
using Atomix.Wallet.BitcoinBased;
using Atomix.Wallet.Ethereum;
using Atomix.Wallet.Tezos;
using Serilog;

namespace Atomix.Wallet
{
    public class HdWalletScanner : IHdWalletScanner
    {
        private IAccount Account { get; }

        public HdWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public async Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var currency in Account.Currencies)
                if (currency.IsTransactionsAvailable)
                    await ScanAsync(currency, skipUsed, cancellationToken)
                        .ConfigureAwait(false);
        }

        public Task ScanAsync(
            Currency currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetCurrencyScanner(currency)
                .ScanAsync(skipUsed, cancellationToken);
        }

        public async Task ScanFreeAddressesAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var currency in Account.Currencies)
            {
                var walletAddress = await Account
                    .GetFreeExternalAddressAsync(currency, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    await GetCurrencyScanner(currency)
                        .ScanAsync(walletAddress.Address, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Free address scan error for currency: {@currency}", currency.Name);
                }
            }
        }

        public async Task ScanAddressAsync(
            Currency currency,
            string address,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                await GetCurrencyScanner(currency)
                    .ScanAsync(address, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Address scan error for currency {@currency} and address {@address}", currency.Name, address);
            }
        }

        private ICurrencyHdWalletScanner GetCurrencyScanner(Currency currency)
        {
            if (currency is BitcoinBasedCurrency)
                return new BitcoinBasedWalletScanner(currency, Account);
            if (currency is Atomix.Ethereum)
                return new EthereumWalletScanner(Account);
            if (currency is Atomix.Tezos)
                return new TezosWalletScanner(Account);

            throw new NotSupportedException($"Currency {currency.Name} not supported");
        }
    }
}