using System;
using System.Threading;
using System.Threading.Tasks;
using Atomex.Core.Entities;
using Atomex.Wallet.Abstract;
using Atomex.Wallet.BitcoinBased;
using Atomex.Wallet.Ethereum;
using Atomex.Wallet.Tezos;
using Serilog;

namespace Atomex.Wallet
{
    public class HdWalletScanner : IHdWalletScanner
    {
        private IAccount Account { get; }

        public HdWalletScanner(IAccount account)
        {
            Account = account ?? throw new ArgumentNullException(nameof(account));
        }

        public Task ScanAsync(
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                foreach (var currency in Account.Currencies)
                    if (currency.IsTransactionsAvailable)
                        await ScanAsync(currency, skipUsed, cancellationToken)
                            .ConfigureAwait(false);
            }, cancellationToken);
        }

        public Task ScanAsync(
            Currency currency,
            bool skipUsed = false,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                await GetCurrencyScanner(currency)
                    .ScanAsync(skipUsed, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken);
        }

        public async Task ScanFreeAddressesAsync(
            CancellationToken cancellationToken = default)
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
            CancellationToken cancellationToken = default)
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
            if (currency is Atomex.Ethereum)
                return new EthereumWalletScanner(Account);
            if (currency is Atomex.Tezos)
                return new TezosWalletScanner(Account);

            throw new NotSupportedException($"Currency {currency.Name} not supported");
        }
    }
}